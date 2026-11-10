namespace SeldonLattice.API.Models;

/// <summary>
/// Aggregated health response for the API and its dependencies.
/// </summary>
public sealed record HealthResponse
{
    /// <summary>Overall health status: "healthy", "degraded", or "unhealthy".</summary>
    public required string Status { get; init; }
    /// <summary>ISO 8601 timestamp of when the server started.</summary>
    public required string Uptime { get; init; }
    /// <summary>Assembly version of the API.</summary>
    public required string Version { get; init; }
    /// <summary>Environment name (Development, Staging, Production).</summary>
    public required string Environment { get; init; }
    /// <summary>Dependency health checks.</summary>
    public required IEnumerable<DependencyHealth> Dependencies { get; init; }
}