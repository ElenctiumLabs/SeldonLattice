namespace SeldonLattice.Domain.Options;

/// <summary>
/// Options for default application values.
/// </summary>
public sealed class DefaultsOptions
{
    /// <summary>Default page size for paginated endpoints.</summary>
    public const int DefaultPage = 50;
    public int DefaultPageSize { get; init; } = DefaultPage;
}