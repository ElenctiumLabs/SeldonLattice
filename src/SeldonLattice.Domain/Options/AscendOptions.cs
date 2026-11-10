namespace SeldonLattice.Domain.Options;

/// <summary>
/// Configuration options for the ascend endpoint.
/// </summary>
public sealed class AscendOptions
{
    /// <summary>Default timeout in seconds for ascend operations.</summary>
    public int TimeoutSeconds { get; init; } = 900;
}