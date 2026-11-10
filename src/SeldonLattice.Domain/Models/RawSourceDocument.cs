namespace SeldonLattice.Domain.Models;

/// <summary>
/// A raw document as returned by a source connector, before normalization.
/// </summary>
public record RawSourceDocument
{
    public string ExternalId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Culture { get; init; } = "unknown";
    public string Domain { get; init; } = "unknown";
    public DateTime SourceTimestamp { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = [];
}