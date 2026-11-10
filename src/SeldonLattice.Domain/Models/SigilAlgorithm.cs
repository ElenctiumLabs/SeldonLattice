namespace SeldonLattice.Domain.Models;

/// <summary>
/// Available sigil generation algorithms. Each algorithm interprets a CID hash
/// to produce a distinct geometric pattern for cryptographic hash visualization.
/// </summary>
public enum SigilAlgorithm
{
    /// <summary>Lissajous curve with hash-derived frequency ratios.</summary>
    Lissajous,
    /// <summary>Mandala pattern with hash-derived rotational symmetry.</summary>
    Mandala,
    /// <summary>Recursive fractal tree with hash-derived angles.</summary>
    FractalTree,
}