using MessagePack;

namespace SeldonLattice.Domain.Models.Storage;

/// <summary>
/// Lightweight authority/source citation returned to agents.
/// </summary>
[MessagePackObject]
public record Citation
{
    [Key(0)] public string Id { get; init; } = string.Empty;
    [Key(1)] public string Title { get; init; } = string.Empty;
    [Key(2)] public string Url { get; init; } = string.Empty;
    [Key(3)] public string SourceId { get; init; } = string.Empty;
    [Key(4)] public double Confidence { get; init; } = 0.0;
}
