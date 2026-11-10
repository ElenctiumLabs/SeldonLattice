namespace SeldonLattice.API.Services.Outbox;

/// <summary>
/// A trace export pending delivery to Sentinel. Written before any network attempt
/// so a Sentinel outage or process crash can never silently lose telemetry;
/// <see cref="SentinelOutboxFlushWorker"/> retries with exponential backoff.
/// </summary>
public sealed class PendingTraceExport
{
    public long Id { get; init; }

    /// <summary>JSON-serialized SentinelTrace payload.</summary>
    public string TraceJson { get; init; } = string.Empty;

    /// <summary>Correlation only - the originating Seldon session id.</summary>
    public string SessionId { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }

    public int AttemptCount { get; set; }

    /// <summary>null = immediately due.</summary>
    public DateTime? NextAttemptAtUtc { get; set; }

    public string? LastError { get; set; }
}
