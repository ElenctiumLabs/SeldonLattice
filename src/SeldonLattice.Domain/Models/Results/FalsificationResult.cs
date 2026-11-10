namespace SeldonLattice.Domain.Models.Results;

/// <summary>
/// Result of a falsification run.
/// </summary>
public record FalsificationResult
{
    public bool BiasDetected { get; init; }
    public BiasReport BiasReport { get; init; } = new();
    public List<IngestionResult> CounterDataResults { get; init; } = [];
    public List<string> GeneratedThemes { get; init; } = [];
    public string Summary { get; init; } = string.Empty;
}