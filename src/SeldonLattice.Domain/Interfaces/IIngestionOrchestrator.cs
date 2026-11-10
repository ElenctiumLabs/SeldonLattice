using SeldonLattice.Domain.Models.Results;
using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Orchestrates the full ingestion pipeline: fetch from connectors,
/// normalize, embed, store, then audit for bias and trigger falsification.
/// </summary>
public interface IIngestionOrchestrator
{
    /// <summary>
    /// Run a full ingestion cycle: fetch from all registered connectors,
    /// normalize, deduplicate, embed, store, and audit.
    /// </summary>
    Task<IngestionCycleResult> RunIngestionCycleAsync(CancellationToken ct);
    
    /// <summary>
    /// Ingest from a specific connector with a targeted query.
    /// Used by the falsifier to pull counter-data.
    /// </summary>
    Task<IngestionResult> IngestFromSourceAsync(string sourceId, string query, int maxResults, CancellationToken ct);
    
    /// <summary>
    /// Run the audit engine to assess current bias in the lattice.
    /// </summary>
    Task<BiasReport> AuditBiasAsync(CancellationToken ct);
    
    /// <summary>
    /// Run the falsifier: if bias is detected, generate falsification themes
    /// and trigger targeted ingestion from under-represented sources.
    /// </summary>
    Task<FalsificationResult> RunFalsificationAsync(CancellationToken ct);
    
    /// <summary>
    /// Get all ingested documents, optionally filtered by source/culture/domain.
    /// </summary>
    Task<IReadOnlyList<IngestedDocument>> GetIngestedDocumentsAsync(string? sourceId = null, string? culture = null, string? domain = null, CancellationToken ct = default);
}