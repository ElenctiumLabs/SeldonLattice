using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Domain.Options;

namespace SeldonLattice.API.Controllers;

/// <summary>
/// Philosophy Dialectics endpoints and the shared legacy SSE streaming route used by all
/// modules (philosophy, research, custom) (GET /api/ascend/stream).
///
/// Historical note: the philosophy module was the first built and previously had bespoke
/// streaming logic here that diverged from the main AscendEndpoint. That divergence is now
/// removed - all modules use the same session-validated, timeout-governed, logged path.
/// </summary>
[ApiController]
[Route("api/dialectics/philosophy")]
public class PhilosophyDialecticsController(
    IDialecticsModuleRegistry registry,
    ISessionService sessionService,
    ICurrentUserService currentUser,
    IOptions<AscendOptions> ascendOptions,
    ILogger<PhilosophyDialecticsController> logger)
    : ControllerBase
{
    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    //  Philosophy batch (non-streaming) 

    /// <summary>
    /// Non-streaming batch ascend for the philosophy module (admin/test use).
    /// </summary>
    [ProducesResponseType(typeof(DialogueLevelResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("ascend")]
    [Authorize(Policy = "AscentOnly")]
    public async Task<IResult> AscendLevel(
        [FromQuery] string sessionId,
        [FromQuery] int level,
        [FromQuery] string theme,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return Results.BadRequest(new { error = "Session ID is required" });
        if (string.IsNullOrWhiteSpace(theme))
            return Results.BadRequest(new { error = "Theme is required" });

        var session = await sessionService.GetSessionAsync(sessionId);
        if (session == null)
            return Results.NotFound(new { error = $"Session '{sessionId}' not found." });

        IDialecticsModule module;
        try { module = registry.GetModule("philosophy", HttpContext.RequestServices); }
        catch (KeyNotFoundException) { return Results.BadRequest(new { error = "Philosophy module is not registered." }); }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(ascendOptions.Value.TimeoutSeconds));

        try
        {
            logger.LogInformation("[Philosophy] Batch ascend: session={SessionId} level={Level} theme='{Theme}'", sessionId, level, theme);
            var result = await module.ExecuteLevelAsync(level, theme, sessionId, session.RootQuestion, cts.Token);

            // Export to Sentinel (fire-and-warn; never breaks the reasoning run).
            try
            {
                var usageCollector = HttpContext.RequestServices.GetRequiredService<SeldonLattice.Infrastructure.Services.ILlmUsageCollector>();
                var (inputTokens, outputTokens, modelId) = usageCollector.GetAndReset(sessionId);
                var traceService = HttpContext.RequestServices.GetService<Services.SessionTraceService>();
                if (traceService is not null)
                {
                    var turns = result.DebateTurnEvents ?? [];
                    if (turns.Count > 0)
                    {
                        await traceService.IngestSessionAsync(
                            sessionId, "philosophy", session.RootQuestion,
                            result, turns,
                            inputTokens: (int)inputTokens,
                            outputTokens: (int)outputTokens,
                            modelId: modelId,
                            ct: CancellationToken.None);
                    }
                    else
                    {
                        await traceService.IngestSessionResultAsync(
                            sessionId, "philosophy", session.RootQuestion,
                            result, (int)inputTokens, (int)outputTokens, modelId, CancellationToken.None);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Philosophy] Sentinel export failed for session {SessionId}", sessionId);
            }

            return Results.Ok(result);
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return Results.Problem("Ascend operation timed out.", statusCode: 504);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Philosophy] Batch ascend failed: session={SessionId} level={Level}", sessionId, level);
            return Results.Problem("Ascend operation failed.", statusCode: 500);
        }
    }

    //  Philosophy streaming (module-specific path) 

    /// <summary>
    /// Streaming ascend for the philosophy module (canonical path for this module).
    /// </summary>
    [HttpGet("ascend/stream")]
    [Authorize(Policy = "StreamOnly")]
    public Task AscendLevelStreaming(
        [FromQuery] string sessionId,
        [FromQuery] int level,
        [FromQuery] string theme,
        CancellationToken ct)
        => SharedModuleStreamingHandler(sessionId, level, theme, ct, module: "philosophy");

    // ---- Shared streaming handler ----------------------------------
    // Called by:
    //   GET /api/dialectics/philosophy/ascend/stream  (philosophy-specific path)
    //   GET /api/ascend/stream?module=<key>           (legacy shared path used by the frontend)
    //
    // This handler is the canonical, production SSE path for every non-psychology
    // module. It validates the session, applies the configured timeout, logs the
    // ascent, and exports to Sentinel on completion - matching the robustness of
    // the POST AscendEndpoint streaming path.

    /// <summary>
    /// Legacy shared SSE route the frontend EventSource always hits for every module.
    /// It accepts a `module` query parameter and dispatches to the correct
    /// IDialecticsModule via the registry - same logic as AscendController's
    /// streaming path, but over GET so EventSource works without a polyfill.
    /// </summary>
    [HttpGet("/api/ascend/stream")]
    [Authorize(Policy = "StreamOnly")]
    public async Task SharedModuleStreamingHandler(
        [FromQuery] string sessionId,
        [FromQuery] int level,
        [FromQuery] string theme,
        CancellationToken ct,
        [FromQuery] string? module = null)
    {
        var moduleKey = string.IsNullOrWhiteSpace(module) ? "philosophy" : module;

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await HttpContext.Response.WriteAsJsonAsync(new { error = "sessionId is required" }, ct);
            return;
        }
        if (string.IsNullOrWhiteSpace(theme))
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await HttpContext.Response.WriteAsJsonAsync(new { error = "theme is required" }, ct);
            return;
        }

        // Validate session ownership - same guard as NodeEndpoints uses.
        var ownerId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var session = await sessionService.GetUserSessionAsync(sessionId, ownerId);
        if (session == null)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            await HttpContext.Response.WriteAsJsonAsync(new { error = $"Session '{sessionId}' not found." }, ct);
            return;
        }

        IDialecticsModule moduleImpl;
        try { moduleImpl = registry.GetModule(moduleKey, HttpContext.RequestServices); }
        catch (KeyNotFoundException)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await HttpContext.Response.WriteAsJsonAsync(
                new { error = $"Module '{moduleKey}' is not registered. Available: {string.Join(", ", registry.GetAvailableModules())}" }, ct);
            return;
        }

        HttpContext.Response.ContentType = "text/event-stream";
        HttpContext.Response.Headers.Append("Cache-Control", "no-cache");
        HttpContext.Response.Headers.Append("Connection", "keep-alive");
        HttpContext.Response.Headers.Append("X-Accel-Buffering", "no");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(ascendOptions.Value.TimeoutSeconds));

        var usageCollector = HttpContext.RequestServices.GetRequiredService<SeldonLattice.Infrastructure.Services.ILlmUsageCollector>();
        var eventId = 0;
        var turns = new List<DebateTurnEvent>();
        var rootTheme = session.RootQuestion;

        logger.LogInformation("[{Module}] SSE stream start: session={SessionId} level={Level} theme='{Theme}'",
            moduleKey, sessionId, level, theme);

        try
        {
            await foreach (var evt in moduleImpl.ExecuteLevelStreamingAsync(level, theme, sessionId, rootTheme, cts.Token))
            {
                turns.Add(evt);
                var json = JsonSerializer.Serialize(evt, SseJsonOptions);
                await HttpContext.Response.WriteAsync($"id: {eventId}\n", cancellationToken: ct);
                await HttpContext.Response.WriteAsync($"data: {json}\n\n", cancellationToken: ct);
                await HttpContext.Response.Body.FlushAsync(ct);
                eventId++;
            }
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            logger.LogWarning("[{Module}] SSE stream timed out after {Timeout}s: session={SessionId} level={Level}",
                moduleKey, ascendOptions.Value.TimeoutSeconds, sessionId, level);
            // Stream already open - send a terminal event so the client knows to stop
            var timeoutEvt = JsonSerializer.Serialize(
                new { type = "system", content = "Stream timed out.", level, exitReason = "Timeout" },
                SseJsonOptions);
            try
            {
                await HttpContext.Response.WriteAsync($"id: {eventId}\n", cancellationToken: CancellationToken.None);
                await HttpContext.Response.WriteAsync($"data: {timeoutEvt}\n\n", cancellationToken: CancellationToken.None);
                await HttpContext.Response.Body.FlushAsync(CancellationToken.None);
            }
            catch { /* response may already be closed */ }
            return;
        }

        logger.LogInformation("[{Module}] SSE stream complete: session={SessionId} level={Level} events={Count}",
            moduleKey, sessionId, level, eventId);

        // Export to Sentinel (fire-and-warn; never breaks the reasoning run).
        try
        {
            var (inputTokens, outputTokens, modelId) = usageCollector.GetAndReset(sessionId);
            var traceService = HttpContext.RequestServices.GetService<Services.SessionTraceService>();
            if (traceService is not null && turns.Count > 0)
            {
                await traceService.IngestSessionAsync(
                    sessionId, moduleKey, rootTheme,
                    result: null, turns,
                    inputTokens: (int)inputTokens,
                    outputTokens: (int)outputTokens,
                    modelId: modelId,
                    ct: CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[{Module}] Sentinel export failed for session {SessionId}", moduleKey, sessionId);
        }
    }
}
