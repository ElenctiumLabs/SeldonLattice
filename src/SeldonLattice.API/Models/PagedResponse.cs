namespace SeldonLattice.API.Models;

/// <summary>
/// Paginated response wrapper for collection endpoints.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed record PagedResponse<T>
{
    /// <summary>The items for the current page.</summary>
    public required IEnumerable<T> Items { get; init; }
    /// <summary>Total count of matching items across all pages.</summary>
    public required int TotalCount { get; init; }
    /// <summary>Current page number (1-based).</summary>
    public required int Page { get; init; }
    /// <summary>Page size (number of items per page).</summary>
    public required int PageSize { get; init; }
    /// <summary>Total number of pages.</summary>
    public required int TotalPages { get; init; }
}