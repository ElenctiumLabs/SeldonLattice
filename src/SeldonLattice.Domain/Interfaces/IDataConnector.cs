namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// A pluggable connector that fetches documents from an external source.
/// Each connector is self-describing - it knows what culture/domain it represents.
/// </summary>
public interface IDataConnector
{
    /// <summary>Unique identifier for this source (e.g. "europeana", "crossref").</summary>
    string SourceId { get; }
    
    /// <summary>Human-readable name of the source.</summary>
    string DisplayName { get; }
    
    /// <summary>The cultural frame this source primarily represents.</summary>
    string Culture { get; }
    
    /// <summary>The domain this source primarily covers.</summary>
    string Domain { get; }
    
    /// <summary>
    /// Fetch documents matching the given query.
    /// </summary>
    /// <param name="query">Search query string.</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Raw documents from the source, before normalization.</returns>
    Task<IReadOnlyList<RawSourceDocument>> FetchAsync(string query, int maxResults, CancellationToken ct);
    
    /// <summary>
    /// Fetch documents targeting a specific culture/domain combination.
    /// Used by the falsifier to pull counter-data.
    /// </summary>
    Task<IReadOnlyList<RawSourceDocument>> FetchByCultureAsync(string culture, string domain, int maxResults, CancellationToken ct);
}