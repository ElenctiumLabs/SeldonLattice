namespace SeldonLattice.Domain.Configuration;

/// <summary>
/// Configuration for moderators in a custom module.
/// </summary>
public sealed record ModeratorConfiguration
{
    public string? ModeratorName { get; init; }
    public string? SelectionStrategy { get; init; }
}