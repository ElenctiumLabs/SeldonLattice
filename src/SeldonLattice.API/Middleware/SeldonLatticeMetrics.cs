using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SeldonLattice.API.Middleware;

/// <summary>
/// Metrics and tracing infrastructure for Seldon Lattice.
/// Provides ActivitySource for distributed tracing and Meter for Prometheus-compatible metrics.
/// </summary>
public sealed class SeldonLatticeMetrics : IDisposable
{
    public const string MeterName = "SeldonLattice";
    public static readonly ActivitySource ActivitySource = new(MeterName, "1.0.0");

    private readonly Meter _meter;

    // Counters
    public Counter<long> RequestsTotal { get; }
    public Counter<long> MerkleStoreOperationsTotal { get; }
    public Counter<long> QualityGateFailures { get; }
    public Counter<long> RateLimitRejected { get; }

    // Histograms
    public Histogram<double> RequestDurationSeconds { get; }
    public Histogram<double> AscendDurationSeconds { get; }
    public Histogram<double> IngestionDurationSeconds { get; }
    public Histogram<double> SearchDurationSeconds { get; }

    // Gauges (backed by atomic long)
    private long _totalSessions;
    private long _totalNodesStored;

    public SeldonLatticeMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        RequestsTotal = _meter.CreateCounter<long>(
            "http_requests_total",
            "HTTP requests total",
            "requests");

        RequestDurationSeconds = _meter.CreateHistogram<double>(
            "http_request_duration_seconds",
            "HTTP request duration in seconds",
            "seconds");

        AscendDurationSeconds = _meter.CreateHistogram<double>(
            "dialectic_ascend_duration_seconds",
            "Duration of dialectical ascensions in seconds",
            "seconds");

        IngestionDurationSeconds = _meter.CreateHistogram<double>(
            "ingestion_cycle_duration_seconds",
            "Duration of ingestion cycles in seconds",
            "seconds");

        SearchDurationSeconds = _meter.CreateHistogram<double>(
            "search_duration_seconds",
            "Duration of search operations in seconds",
            "seconds");

        MerkleStoreOperationsTotal = _meter.CreateCounter<long>(
            "merkle_store_operations_total",
            "Total Merkle store operations by type",
            "operations");

        QualityGateFailures = _meter.CreateCounter<long>(
            "quality_gate_failures_total",
            "Total quality gate failures",
            "failures");

        RateLimitRejected = _meter.CreateCounter<long>(
            "rate_limit_rejected_total",
            "Total requests rejected by rate limiter",
            "requests");
    }

    public void IncrementSessions() => Interlocked.Increment(ref _totalSessions);
    public void IncrementNodes() => Interlocked.Increment(ref _totalNodesStored);
    public void RecordAscendDuration(double seconds) => AscendDurationSeconds.Record(seconds);
    public void RecordIngestionDuration(double seconds) => IngestionDurationSeconds.Record(seconds);
    public void RecordSearchDuration(double seconds) => SearchDurationSeconds.Record(seconds);
    public void RecordMerkleOp(string operation) => MerkleStoreOperationsTotal.Add(1, new[] { new KeyValuePair<string, object?>("operation", operation) });
    public void RecordQualityGateFailure() => QualityGateFailures.Add(1);
    public void RecordRateLimitRejection() => RateLimitRejected.Add(1);

    public void Dispose()
    {
        _meter?.Dispose();
        ActivitySource?.Dispose();
    }
}