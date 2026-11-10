namespace SeldonLattice.Domain.Options;

/// <summary>
/// Options for sigil generation defaults.
/// </summary>
public sealed class SigilOptions
{
    /// <summary>Default output size (width = height) in pixels.</summary>
    public int DefaultSize { get; init; } = 256;
    /// <summary>Maximum allowed output size to prevent abuse.</summary>
    public int MaxSize { get; init; } = 1024;
}