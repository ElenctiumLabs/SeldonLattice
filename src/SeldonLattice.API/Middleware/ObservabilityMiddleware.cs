using System.Diagnostics;

namespace SeldonLattice.API.Middleware;

/// <summary>
/// Middleware that assigns a correlation ID to each request (server-generated,
/// inbound value echoed as X-Received-Request-Id), starts a stopwatch for duration
/// measurement, records Prometheus-style metrics, and emits an OpenTelemetry
/// Activity for distributed tracing. Also propagates the X-Request-Id header.
/// </summary>
public sealed class ObservabilityMiddleware(
    RequestDelegate next,
    ILogger<ObservabilityMiddleware> logger,
    SeldonLatticeMetrics metrics)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // Correlation IDs are always generated server-side: an inbound
        // X-Request-Id is advisory only and echoed back in X-Received-Request-Id, so a
        // client can never spoof the ID that lands in logs and traces.
        var receivedRequestId = context.Request.Headers["X-Request-Id"].FirstOrDefault();
        var requestId = Guid.NewGuid().ToString();

        context.TraceIdentifier = requestId;
        context.Response.Headers["X-Request-Id"] = requestId;
        if (!string.IsNullOrEmpty(receivedRequestId))
            context.Response.Headers["X-Received-Request-Id"] = receivedRequestId;
        context.Items["RequestId"] = requestId;

        using var activity = SeldonLatticeMetrics.ActivitySource.StartActivity(
            $"{context.Request.Method} {context.Request.Path}");
        activity?.SetTag("http.request_id", requestId);
        activity?.SetTag("http.method", context.Request.Method);
        activity?.SetTag("http.url", context.Request.Path);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            var durationSeconds = stopwatch.Elapsed.TotalSeconds;
            var statusCode = context.Response.StatusCode;

            metrics.RequestsTotal.Add(1,
                new KeyValuePair<string, object?>("method", context.Request.Method),
                new KeyValuePair<string, object?>("status_code", statusCode),
                new KeyValuePair<string, object?>("path", context.Request.Path));

            metrics.RequestDurationSeconds.Record(durationSeconds,
                new KeyValuePair<string, object?>("method", context.Request.Method),
                new KeyValuePair<string, object?>("status_code", statusCode),
                new KeyValuePair<string, object?>("path", context.Request.Path));

            activity?.SetTag("http.status_code", statusCode);
            activity?.SetTag("http.duration", durationSeconds);

            logger.LogInformation(
                "HTTP {Method} {Path} responded {StatusCode} in {DurationMs}ms with RequestId {RequestId}",
                context.Request.Method,
                context.Request.Path,
                statusCode,
                stopwatch.ElapsedMilliseconds,
                requestId);
        }
    }
}