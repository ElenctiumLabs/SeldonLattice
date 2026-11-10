using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace SeldonLattice.API.Controllers;

/// <summary>
/// Controller for exposing Prometheus-compatible metrics for monitoring.
/// </summary>
[ApiController]
[Route("api/metrics")]
[DisableRateLimiting] // scrapers must never be throttled
public class MetricsController : ControllerBase
{
    /// <summary>
    /// Exposes Prometheus-compatible metrics for monitoring.
    /// </summary>
    [HttpGet]
    public ContentResult GetMetrics()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# HELP seldon_lattice_up Seldon Lattice API status (1 = up)");
        sb.AppendLine("# TYPE seldon_lattice_up gauge");
        sb.AppendLine("seldon_lattice_up 1");
        sb.AppendLine();
        sb.AppendLine("# HELP active_sessions Number of active dialectical sessions");
        sb.AppendLine("# TYPE active_sessions gauge");
        sb.AppendLine("active_sessions 0");
        sb.AppendLine();
        sb.AppendLine("# HELP merkle_store_operations_total Total Merkle store operations");
        sb.AppendLine("# TYPE merkle_store_operations_total counter");
        sb.AppendLine("merkle_store_operations_total 0");
        sb.AppendLine();
        sb.AppendLine("# HELP quality_gate_failures_total Total quality gate failures");
        sb.AppendLine("# TYPE quality_gate_failures_total counter");
        sb.AppendLine("quality_gate_failures_total 0");
        sb.AppendLine();
        sb.AppendLine("# HELP http_requests_total HTTP requests total");
        sb.AppendLine("# TYPE http_requests_total counter");
        sb.AppendLine("http_requests_total 0");
        sb.AppendLine();
        sb.AppendLine("# HELP dialectic_ascend_duration_seconds Duration of ascents in seconds");
        sb.AppendLine("# TYPE dialectic_ascend_duration_seconds histogram");
        sb.AppendLine("dialectic_ascend_duration_seconds_bucket{le=\"0.1\"} 0");
        sb.AppendLine("dialectic_ascend_duration_seconds_bucket{le=\"0.5\"} 0");
        sb.AppendLine("dialectic_ascend_duration_seconds_bucket{le=\"1.0\"} 0");
        sb.AppendLine("dialectic_ascend_duration_seconds_bucket{le=\"5.0\"} 0");
        sb.AppendLine("dialectic_ascend_duration_seconds_bucket{le=\"10.0\"} 0");
        sb.AppendLine("dialectic_ascend_duration_seconds_bucket{le=\"+Inf\"} 0");
        sb.AppendLine("dialectic_ascend_duration_seconds_count 0");
        sb.AppendLine("dialectic_ascend_duration_seconds_sum 0");
        sb.AppendLine();
        sb.AppendLine("# HELP ingestion_cycle_duration_seconds Duration of ingestion cycles in seconds");
        sb.AppendLine("# TYPE ingestion_cycle_duration_seconds histogram");
        sb.AppendLine("ingestion_cycle_duration_seconds_bucket{le=\"0.1\"} 0");
        sb.AppendLine("ingestion_cycle_duration_seconds_bucket{le=\"0.5\"} 0");
        sb.AppendLine("ingestion_cycle_duration_seconds_bucket{le=\"1.0\"} 0");
        sb.AppendLine("ingestion_cycle_duration_seconds_bucket{le=\"5.0\"} 0");
        sb.AppendLine("ingestion_cycle_duration_seconds_bucket{le=\"10.0\"} 0");
        sb.AppendLine("ingestion_cycle_duration_seconds_bucket{le=\"+Inf\"} 0");
        sb.AppendLine("ingestion_cycle_duration_seconds_count 0");
        sb.AppendLine("ingestion_cycle_duration_seconds_sum 0");
        sb.AppendLine();
        sb.AppendLine("# HELP total_nodes_stored Total nodes stored in Merkle DAG");
        sb.AppendLine("# TYPE total_nodes_stored gauge");
        sb.AppendLine("total_nodes_stored 0");
        return Content(sb.ToString(), "text/plain; version=0.0.4; charset=utf-8");
    }
}

