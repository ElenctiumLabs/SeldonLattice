namespace SeldonLattice.API.Models;

/// <summary>
/// Response DTO for sigil data in JSON format (for frontend rendering).
/// </summary>
public sealed record Sigil
{
    /// <summary>The algorithm used to generate this sigil.</summary>
    public required string Algorithm { get; init; }
    /// <summary>The CID that was used as input.</summary>
    public required string Cid { get; init; }
    /// <summary>SVG markup for the sigil.</summary>
    public required string Svg { get; init; }
    /// <summary>Width/height of the output in pixels.</summary>
    public required int Size { get; init; }
}