namespace SeldonLattice.Domain.Models.Results;

/// <summary>
/// Result of a full ingestion cycle across all connectors.
/// </summary>
public record IngestionCycleResult
{
    public List<IngestionResult> SourceResults { get; init; } = [];
    public int TotalNewDocuments { get; init; }
    public int TotalDuplicatesSkipped { get; init; }
    public int TotalErrors { get; init; }
    public BiasReport? BiasReport { get; init; }
    public FalsificationResult? FalsificationResult { get; init; }
    public TimeSpan Duration { get; init; }
}