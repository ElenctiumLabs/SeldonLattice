using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SeldonLattice.API.Models;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Domain.Options;
using SeldonLattice.Infrastructure.Services;

namespace SeldonLattice.API.Controllers;

/// <summary>
/// Endpoints for triggering dialectical debate ascents.
/// </summary>
[ApiController]
[Route("api/ascend")]
public class AscendController(
    IDialecticsModuleRegistry registry,
    ISessionService sessionService,
    ICurrentUserService currentUser,
    IOptions<AscendOptions> ascendOptions,
    ILogger<AscendController> logger)
    : ControllerBase
{
    private readonly IDialecticsModuleRegistry _registry = registry;
    private readonly ISessionService _sessionService = sessionService;
    private readonly ICurrentUserService _currentUser = currentUser;
    private readonly IOptions<AscendOptions> _ascendOptions = ascendOptions;
    private readonly ILogger<AscendController> _logger = logger;

    /// <summary>
    /// Triggers a dialectical ascent and returns the result as a blocking call.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "AscentOnly")]
    public async Task<IResult> AscendLevel(
        [FromBody] AscendRequest req,
        HttpContext httpContext,
        IDialecticsModuleRegistry registry,
        ISessionService sessionService,
        ICurrentUserService currentUser,
        [FromServices] IOptions<AscendOptions> ascendOptions,
        [FromServices] ILogger logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Theme))
            return Results.BadRequest(new { error = "Theme is required" });
        if (req.Level < 1)
            return Results.BadRequest(new { error = "Level must be >= 1" });

        string sessionId;
        string rootTheme;
        var ownerId = currentUser.UserId ?? string.Empty;
        var framework = req.Module;

        try
        {
            if (!string.IsNullOrEmpty(req.SessionId))
            {
                var existing = await sessionService.GetSessionAsync(req.SessionId);
                if (existing != null)
                {
                    sessionId = req.SessionId;
                    rootTheme = req.RootTheme ?? existing.RootQuestion;
                }
                else
                {
                    logger.LogWarning("Session {SessionId} not found; creating new session with theme '{Theme}'", req.SessionId, req.Theme);
                    sessionId = await sessionService.CreateSessionAsync(req.RootTheme ?? req.Theme, framework, ownerId);
                    rootTheme = req.RootTheme ?? req.Theme;
                }
            }
            else
            {
                sessionId = await sessionService.CreateSessionAsync(req.RootTheme ?? req.Theme, framework, ownerId);
                rootTheme = req.RootTheme ?? req.Theme;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve or create session for ascend. Theme={Theme}, Level={Level}, Module={Module}", req.Theme, req.Level, req.Module);
            return Results.Problem("Failed to initialize session.", statusCode: 500);
        }

        IDialecticsModule module;
        try
        {
            module = registry.GetModule(req.Module, httpContext.RequestServices);
        }
        catch (KeyNotFoundException)
        {
            logger.LogWarning("Module '{Module}' not found. Available: {Modules}", req.Module, string.Join(", ", registry.GetAvailableModules()));
            return Results.BadRequest(new { error = $"Module '{req.Module}' is not registered. Available: {string.Join(", ", registry.GetAvailableModules())}" });
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(ascendOptions.Value.TimeoutSeconds));

        try
        {
            logger.LogInformation("Starting ascend: session={SessionId}, level={Level}, theme='{Theme}', module='{Module}'", sessionId, req.Level, req.Theme, req.Module);
            var result = await module.ExecuteLevelAsync(req.Level, req.Theme, sessionId, rootTheme, cts.Token);
            logger.LogInformation("Ascend completed: session={SessionId}, level={Level}, synthesisHash={SynthesisHash}", sessionId, req.Level, result.SynthesisHash);

            await ExportTraceAsync(httpContext, sessionId, framework, rootTheme, result, logger, ct: CancellationToken.None);
            return Results.Ok(result);
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Ascend timed out after {Timeout}s: session={SessionId}, level={Level}", ascendOptions.Value.TimeoutSeconds, sessionId, req.Level);
            return Results.Problem("Ascend operation timed out.", statusCode: 504);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ascend failed: session={SessionId}, level={Level}, module={Module}", sessionId, req.Level, req.Module);
            return Results.Problem("Ascend operation failed.", statusCode: 500);
        }
    }

    /// <summary>
    /// Triggers a streaming dialectical ascent using Server-Sent Events.
    /// Emits events for each phase: thesis, antithesis, mediation, synthesis, gate, complete.
    /// </summary>
    [HttpPost("stream")]
    [Authorize(Policy = "StreamOnly")]
    public async Task AscendLevelStreaming(
        [FromBody] AscendRequest req,
        IDialecticsModuleRegistry registry,
        ISessionService sessionService,
        ICurrentUserService currentUser,
        [FromServices] IOptions<AscendOptions> ascendOptions,
        [FromServices] ILogger logger,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Theme))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsJsonAsync(new { error = "Theme is required" }, ct);
            return;
        }

        IDialecticsModule module;
        try
        {
            module = registry.GetModule(req.Module, httpContext.RequestServices);
        }
        catch (KeyNotFoundException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsJsonAsync(new { error = $"Module '{req.Module}' is not registered" }, ct);
            return;
        }

        string sessionId;
        string rootTheme;
        var ownerId = currentUser.UserId ?? string.Empty;
        var framework = req.Module;

        try
        {
            if (!string.IsNullOrEmpty(req.SessionId))
            {
                var existing = await sessionService.GetSessionAsync(req.SessionId);
                sessionId = existing != null ? req.SessionId : await sessionService.CreateSessionAsync(req.RootTheme ?? req.Theme, framework, ownerId);
                rootTheme = req.RootTheme ?? (existing?.RootQuestion ?? req.Theme);
            }
            else
            {
                sessionId = await sessionService.CreateSessionAsync(req.RootTheme ?? req.Theme, framework, ownerId);
                rootTheme = req.RootTheme ?? req.Theme;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve or create session for streaming ascend");
            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await httpContext.Response.WriteAsJsonAsync(new { error = "Failed to initialize session" }, ct);
            return;
        }

        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.Append("Cache-Control", "no-cache");
        httpContext.Response.Headers.Append("Connection", "keep-alive");
        httpContext.Response.Headers.Append("X-Accel-Buffering", "no");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(ascendOptions.Value.TimeoutSeconds));

        var eventId = 0;
        var turns = new List<DebateTurnEvent>();
        await foreach (var evt in module.ExecuteLevelStreamingAsync(req.Level, req.Theme, sessionId, rootTheme, cts.Token))
        {
            turns.Add(evt);
            var json = JsonSerializer.Serialize(evt, SseJsonOptions);
            await httpContext.Response.WriteAsync($"id: {eventId}\n", cancellationToken: ct);
            await httpContext.Response.WriteAsync($"data: {json}\n\n", cancellationToken: ct);
            await httpContext.Response.Body.FlushAsync(ct);
            eventId++;
        }

        // Export the completed level to Sentinel (no-op unless integration is enabled).
        await ExportTraceAsync(httpContext, sessionId, framework, rootTheme, result: null, logger, turns: turns, ct: CancellationToken.None);
    }

    /// <summary>
    /// Fire-and-warn export of a completed ascent level to Sentinel. Resolves
    /// <see cref="Services.SessionTraceService"/> from request services; returns silently
    /// when the Sentinel integration is disabled.
    /// </summary>
    private static async Task ExportTraceAsync(
        HttpContext httpContext,
        string sessionId,
        string framework,
        string rootQuestion,
        DialogueLevelResult? result,
        ILogger logger,
        IReadOnlyList<DebateTurnEvent>? turns = null,
        CancellationToken ct = default)
    {
        try
        {
            var traceService = httpContext.RequestServices.GetService<Services.SessionTraceService>();
            if (traceService is null)
                return;

            var usageCollector = httpContext.RequestServices.GetService<ILlmUsageCollector>();
            var (inputTokens, outputTokens, modelId) = usageCollector?.GetAndReset(sessionId) ?? (0, 0, string.Empty);

            if (turns is { Count: > 0 })
                await traceService.IngestSessionAsync(sessionId, framework, rootQuestion, result, turns,
                    inputTokens: (int)inputTokens, outputTokens: (int)outputTokens, modelId: modelId, ct: ct);
            else if (result?.DebateTurnEvents is { Count: > 0 })
                await traceService.IngestSessionAsync(sessionId, framework, rootQuestion, result, result.DebateTurnEvents,
                    inputTokens: (int)inputTokens, outputTokens: (int)outputTokens, modelId: modelId, ct: ct);
            else if (result is not null)
                await traceService.IngestSessionResultAsync(sessionId, framework, rootQuestion, result,
                    (int)inputTokens, (int)outputTokens, modelId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Sentinel trace export failed for session {SessionId}", sessionId);
        }
    }

    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
}
