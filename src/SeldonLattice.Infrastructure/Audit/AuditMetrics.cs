using System.Diagnostics.Metrics;

namespace SeldonLattice.Infrastructure.Audit;

/// <summary>
/// Metrics instruments for LLM audit interception telemetry.
/// </summary>
public sealed class AuditMetrics
{
    private static readonly Meter Meter = new("SeldonLattice.Audit", "1.0");

    public readonly Counter<long> AuditWriteFailures =
        Meter.CreateCounter<long>("audit_write_failures_total",
            description: "Number of LlmAuditNode write failures");

    public readonly Histogram<long> AuditNodeLatency =
        Meter.CreateHistogram<long>("audit_node_latency_ms",
            unit: "ms",
            description: "Wall-clock latency of each intercepted LLM call");
}
