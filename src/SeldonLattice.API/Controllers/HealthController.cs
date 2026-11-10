using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SeldonLattice.API.Models;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.API.Controllers;

/// <summary>
/// Controller for health checking the API and its dependencies.
/// </summary>
[ApiController]
[Route("api/health")]
[DisableRateLimiting] // probes must never be throttled.
public class HealthController(
    AppInfo appInfo,
    IMerkleStore store,
    IChatService chatService,
    IConfiguration config,
    ILogger<HealthController> logger)
    : ControllerBase
{
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Reports the status of the API and its dependencies (Merkle store, LLM provider, ingestion connectors).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetHealth(CancellationToken ct)
    {
        var deps = new List<DependencyHealth>();

        // Check Merkle store
        var merkleStatus = await CheckMerkleStore(ct);
        deps.Add(merkleStatus);

        // Check LLM provider
        var llmStatus = await CheckLlmProvider(ct);
        deps.Add(llmStatus);

        // Check ingestion connectors
        var ingestionStatus = CheckIngestionConnectors();
        deps.Add(ingestionStatus);

        // Determine overall status
        var overallStatus = deps.All(d => d.Status == "healthy")
            ? "healthy"
            : deps.Any(d => d.Status == "unhealthy") ? "unhealthy" : "degraded";

        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

        var response = new HealthResponse
        {
            Status = overallStatus,
            Uptime = appInfo.StartTimeIso,
            Version = version,
            Environment = appInfo.GetType().Assembly.GetName().Version?.ToString() ?? "unknown",
            Dependencies = deps
        };

        return Ok(response);
    }

    private async Task<DependencyHealth> CheckMerkleStore(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(HealthCheckTimeout);
            var nodes = await store.GetAllNodesAsync<DialecticalNode>();
            var count = nodes?.Count() ?? 0;
            return new DependencyHealth("merkleStore", "healthy", $"Readable ({count} nodes)");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Merkle store health check failed");
            return new DependencyHealth("merkleStore", "unhealthy", ex.Message);
        }
    }

    private async Task<DependencyHealth> CheckLlmProvider(CancellationToken ct)
    {
        CancellationTokenSource? cts = null;
        try
        {
            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(HealthCheckTimeout);
            var response = await chatService.GetResponseAsync("Ping", cts.Token);
            if (!string.IsNullOrEmpty(response))
                return new DependencyHealth("llmProvider", "healthy", "Responded to ping");
            return new DependencyHealth("llmProvider", "degraded", "Empty response");
        }
        catch (OperationCanceledException) when (cts?.Token.IsCancellationRequested == true)
        {
            return new DependencyHealth("llmProvider", "unhealthy", "Health check timed out");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LLM provider health check failed");
            return new DependencyHealth("llmProvider", "unhealthy", ex.Message);
        }
        finally
        {
            cts?.Dispose();
        }
    }

    private DependencyHealth CheckIngestionConnectors()
    {
        var configuredConnectors = new List<string>();

        if (!string.IsNullOrEmpty(config["Ingestion:Europeana:ApiKey"]))
            configuredConnectors.Add("Europeana");

        configuredConnectors.Add("YahooFinance");

        if (configuredConnectors.Count == 0)
            return new DependencyHealth("ingestionConnectors", "degraded", "No connectors configured");

        return new DependencyHealth("ingestionConnectors", "healthy", $"Configured: {string.Join(", ", configuredConnectors)}");
    }
}
