using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace SeldonLattice.API.Middleware;

/// <summary>
/// A sliding-window rate limiter that tracks requests per client (API key or IP).
/// Uses an in-memory sliding window algorithm with per-client request timestamps.
/// </summary>
public sealed class RateLimitMiddleware(
    RequestDelegate next,
    IOptions<RateLimitOptions> options,
    ILogger<RateLimitMiddleware> logger)
{
    private readonly RateLimitOptions _options = options.Value;

    /// <summary>In-flight request counts and timestamps per client ID.</summary>
    private readonly ConcurrentDictionary<string, ClientRateState> _clients = new();

    /// <summary>Interval at which expired entries are cleaned up.</summary>
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(1);
    private DateTime _lastCleanup = DateTime.UtcNow;

    public async Task InvokeAsync(HttpContext context)
    {
        var clientId = GetClientId(context);
        var now = DateTime.UtcNow;
        var window = TimeSpan.FromSeconds(RateLimitOptions.WindowSizeSeconds);

        var state = _clients.GetOrAdd(clientId, _ => new ClientRateState());
        state.AddRequest(now);

        // Trim requests outside the sliding window
        state.TrimOldRequests(now - window);

        var countInWindow = state.RequestCount;
        var remaining = Math.Max(0, RateLimitOptions.RequestsPerMinute - countInWindow);

        // Burst check: requests in the last second
        var burstCount = state.GetBurstCount(now, TimeSpan.FromSeconds(1));
        var allowed = countInWindow <= RateLimitOptions.RequestsPerMinute && burstCount <= RateLimitOptions.BurstLimit;

        // Always add rate limit headers
        context.Response.Headers["X-RateLimit-Limit"] = RateLimitOptions.RequestsPerMinute.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = remaining.ToString();
        context.Response.Headers["X-RateLimit-Reset"] = new DateTimeOffset(now + window).ToUnixTimeSeconds().ToString();

        if (!allowed)
        {
            logger.LogWarning("Rate limit exceeded for client {ClientId}: {Count} requests in window", clientId, countInWindow);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = (window.TotalSeconds / 60).ToString("0");
            context.Response.Headers["X-RateLimit-Remaining"] = "0";

            context.Response.ContentType = "application/json";
            var error = System.Text.Json.JsonSerializer.Serialize(new
            {
                error = "rate_limit_exceeded",
                message = "Too many requests. Please retry after the specified interval.",
                limit = RateLimitOptions.RequestsPerMinute,
                windowSeconds = RateLimitOptions.WindowSizeSeconds
            });
            await context.Response.WriteAsync(error);
            return;
        }

        // Periodic cleanup of stale client states
        if ((now - _lastCleanup).TotalMinutes >= _cleanupInterval.TotalMinutes)
        {
            CleanupStaleClients(now - window);
            _lastCleanup = now;
        }

        await next(context);
    }

    private static string GetClientId(HttpContext context)
    {
        // Prefer API key over IP if available
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader))
        {
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return $"key:{authHeader["Bearer ".Length..].Trim()}";
            if (authHeader.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
                return $"key:{authHeader["ApiKey ".Length..].Trim()}";
        }

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"ip:{ip}";
    }

    private void CleanupStaleClients(DateTime threshold)
    {
        foreach (var kvp in _clients)
        {
            if (kvp.Value.LastRequest < threshold)
            {
                _clients.TryRemove(kvp.Key, out _);
            }
        }
    }

    /// <summary>
    /// Per-client state tracking request timestamps for sliding-window rate limiting.
    /// </summary>
    private sealed class ClientRateState
    {
        private readonly List<DateTime> _requests = [];
        public DateTime LastRequest { get; private set; } = DateTime.MinValue;

        public int RequestCount => _requests.Count;

        public void AddRequest(DateTime timestamp)
        {
            lock (_requests)
            {
                _requests.Add(timestamp);
                LastRequest = timestamp;
            }
        }

        public void TrimOldRequests(DateTime cutoff)
        {
            lock (_requests)
            {
                _requests.RemoveAll(r => r < cutoff);
            }
        }

        public int GetBurstCount(DateTime now, TimeSpan window)
        {
            lock (_requests)
            {
                return _requests.Count(r => r >= now - window);
            }
        }
    }
}