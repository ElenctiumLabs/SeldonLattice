namespace SeldonLattice.Domain.Models.Results;

/// <summary>
/// The result of ingesting documents from a single source connector.
/// </summary>
public record IngestionResult
{
    public string SourceId { get; init; } = string.Empty;
    public int NewDocuments { get; init; }
    public int DuplicatesSkipped { get; init; }
    public int Errors { get; init; }
    public string? ErrorMessage { get; init; }
    public List<string> DocumentHashes { get; init; } = [];
}