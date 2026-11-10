namespace SeldonLattice.Domain.Models.Results;

/// <summary>
/// Result of sigil generation including the SVG representation and raw data.
/// </summary>
public sealed record SigilResult
{
    /// <summary>The algorithm used to generate this sigil.</summary>
    public required SigilAlgorithm Algorithm { get; init; }
    /// <summary>The CID that was used as input.</summary>
    public required string Cid { get; init; }
    /// <summary SVG markup for the sigil.</summary>
    public required string Svg { get; init; }
    /// <summary>Width/height of the output in pixels.</summary>
    public required int Size { get; init; }
    /// <summary>Hash bytes used to seed the deterministic pattern.</summary>
    public required byte[] HashBytes { get; init; }
}