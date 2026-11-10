namespace SeldonLattice.Domain.Configuration;

/// <summary>
/// Configuration for mediators in a custom module.
/// </summary>
public sealed record MediatorConfigudation
{
    public string[]? MediatorNames { get; init; }
    public string? Mode { get; init; }
}