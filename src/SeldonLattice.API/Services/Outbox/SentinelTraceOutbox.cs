using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sentinel.Sdk.Observability;

namespace SeldonLattice.API.Services.Outbox;

public interface ISentinelTraceOutbox
{
    /// <summary>Persists a trace for guaranteed delivery, then attempts an immediate flush.</summary>
    Task EnqueueAsync(SentinelTrace trace, string sessionId, CancellationToken ct = default);

    /// <summary>Delivers all due pending traces. Returns the number successfully sent.</summary>
    Task<int> FlushDueAsync(CancellationToken ct = default);

    /// <summary>Number of traces still awaiting delivery (for /health and diagnostics).</summary>
    Task<int> CountPendingAsync(CancellationToken ct = default);
}

/// <summary>
/// SQLite-backed outbox implementing at-least-once delivery to Sentinel's trace
/// ingestion endpoint. Traces are persisted before the network attempt; successful
/// delivery deletes the row, failures back off exponentially (capped at 1 hour).
/// </summary>
public sealed class SentinelTraceOutbox(
    IDbContextFactory<TraceOutboxDbContext> dbFactory,
    ObservabilityClient observabilityClient,
    IOptions<SentinelConfiguration> config,
    ILogger<SentinelTraceOutbox> logger) : ISentinelTraceOutbox
{
    public IOptions<SentinelConfiguration> Config { get; } = config;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly int BatchSize = 50;
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    public async Task EnqueueAsync(SentinelTrace trace, string sessionId, CancellationToken ct = default)
    {
        var pending = new PendingTraceExport
        {
            TraceJson = JsonSerializer.Serialize(trace, SerializerOptions),
            SessionId = sessionId,
            CreatedAtUtc = DateTime.UtcNow
        };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.PendingTraces.Add(pending);
        await db.SaveChangesAsync(ct);
        logger.LogDebug("Queued Sentinel trace export for session {SessionId} (outbox id {OutboxId})",
            sessionId, pending.Id);
    }

    public async Task<int> FlushDueAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var delivered = 0;

        while (!ct.IsCancellationRequested)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var batch = await db.PendingTraces
                .Where(p => p.NextAttemptAtUtc == null || p.NextAttemptAtUtc <= now)
                .OrderBy(p => p.CreatedAtUtc)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0)
                break;

            // Group by sessionId so all spans for one session go in one batch call.
            var sessionGroups = batch.GroupBy(p => p.SessionId);

            foreach (var group in sessionGroups)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var traces = new List<SentinelTrace>();
                    foreach (var pending in group)
                    {
                        var trace = JsonSerializer.Deserialize<SentinelTrace>(pending.TraceJson, SerializerOptions)
                            ?? throw new InvalidOperationException("Deserialized trace was null.");
                        traces.Add(trace);
                    }

                    await observabilityClient.IngestTracesAsync(traces, ct);
                    foreach (var pending in group)
                    {
                        db.PendingTraces.Remove(pending);
                        delivered++;
                    }
                    logger.LogDebug("Delivered {Count} queued traces for session {SessionId}",
                        traces.Count, group.Key);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    foreach (var pending in group)
                    {
                        pending.AttemptCount++;
                        pending.LastError = ex.Message.Length > 1024 ? ex.Message[..1024] : ex.Message;
                        pending.NextAttemptAtUtc = DateTime.UtcNow + Backoff(pending.AttemptCount);
                    }
                    logger.LogWarning("Sentinel batch delivery failed for session {SessionId} (attempt {Attempt}, next retry {NextRetry:u}): {Error}",
                        group.Key, group.First().AttemptCount, group.First().NextAttemptAtUtc, ex.Message);
                }
            }

            await db.SaveChangesAsync(ct);
            now = DateTime.UtcNow;
        }

        return delivered;
    }

    public async Task<int> CountPendingAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PendingTraces.CountAsync(ct);
    }

    private static TimeSpan Backoff(int attempt) =>
        attempt <= 1 ? TimeSpan.FromSeconds(15) : TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt), MaxBackoff.TotalSeconds));
}
