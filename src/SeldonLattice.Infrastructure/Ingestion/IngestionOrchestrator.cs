using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models;
using SeldonLattice.Domain.Models.Results;
using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Infrastructure.Ingestion;

/// <summary>
/// Orchestrates the full ingestion pipeline:
/// 1. Fetch raw documents from all registered connectors
/// 2. Normalize and embed them into IngestedDocuments
/// 3. Store in the Merkle DAG (deduplicating by ExternalId + SourceId)
/// 4. Audit for bias
/// 5. If bias detected, trigger falsification: pull counter-data from under-represented sources
/// 
/// This is the growth mechanism of the Seldon Lattice - without it,
/// the lattice only debates themes the user manually types in.
/// </summary>
public class IngestionOrchestrator(
    IEnumerable<IDataConnector> connectors,
    DocumentNormalizer normalizer,
    AuditEngine auditEngine,
    IMerkleStore merkleStore,
    ILogger<IngestionOrchestrator> logger,
    IConfiguration configuration)
    : IIngestionOrchestrator
{
    [Experimental("SKEXP0001")]
    public async Task<IngestionCycleResult> RunIngestionCycleAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("Starting ingestion cycle with {Count} connectors", connectors.Count());

        // Step 1: Fetch from all connectors
        var sourceResults = new List<IngestionResult>();
        var defaultMaxResults = configuration.GetValue("Defaults:IngestionMaxResults", 50);
        foreach (var connector in connectors)
        {
            try
            {
                var result = await IngestFromSourceAsync(connector.SourceId, 
                    $"{connector.Domain} {connector.Culture} history", defaultMaxResults, ct);
                sourceResults.Add(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ingestion from {SourceId} failed", connector.SourceId);
                sourceResults.Add(new IngestionResult
                {
                    SourceId = connector.SourceId,
                    Errors = 1,
                    ErrorMessage = ex.Message
                });
            }
        }

        // Step 2: Audit for bias
        var biasReport = await auditEngine.AuditAsync(ct);

        // Step 3: Run falsification if bias detected
        var biasThreshold = configuration.GetValue("Ingestion:BiasThreshold", 0.5f);
        FalsificationResult? falsificationResult = null;
        if (biasReport.BiasScore > biasThreshold)
        {
            logger.LogWarning("Bias detected (score={BiasScore:F2}). Triggering falsification.", biasReport.BiasScore);
            falsificationResult = await RunFalsificationAsync(ct);
        }

        sw.Stop();

        var cycleResult = new IngestionCycleResult
        {
            SourceResults = sourceResults,
            TotalNewDocuments = sourceResults.Sum(r => r.NewDocuments),
            TotalDuplicatesSkipped = sourceResults.Sum(r => r.DuplicatesSkipped),
            TotalErrors = sourceResults.Sum(r => r.Errors),
            BiasReport = biasReport,
            FalsificationResult = falsificationResult,
            Duration = sw.Elapsed
        };

        logger.LogInformation(
            "Ingestion cycle complete: {New} new, {Dup} duplicates, {Err} errors, bias={Bias:F2}, duration={Duration}",
            cycleResult.TotalNewDocuments, cycleResult.TotalDuplicatesSkipped,
            cycleResult.TotalErrors, biasReport.BiasScore, sw.Elapsed);

        return cycleResult;
    }

    [Experimental("SKEXP0001")]
    public async Task<IngestionResult> IngestFromSourceAsync(string sourceId, string query, int maxResults, CancellationToken ct)
    {
        var connector = connectors.FirstOrDefault(c => c.SourceId == sourceId);
        if (connector == null)
        {
            logger.LogWarning("No connector registered for source: {SourceId}", sourceId);
            return new IngestionResult { SourceId = sourceId, Errors = 1, ErrorMessage = $"No connector for '{sourceId}'" };
        }

        // Fetch raw documents
        var rawDocs = await connector.FetchAsync(query, maxResults, ct);
        if (rawDocs.Count == 0)
            return new IngestionResult { SourceId = sourceId, NewDocuments = 0 };

        // Get existing documents for deduplication
        var existingDocs = (await merkleStore.GetAllNodesAsync<IngestedDocument>())
            .Where(d => d.SourceId == sourceId)
            .ToLookup(d => d.ExternalId);

        var newDocs = 0;
        var duplicates = 0;
        var errors = 0;
        var hashes = new List<string>();

        foreach (var raw in rawDocs)
        {
            try
            {
                // Deduplicate by ExternalId + SourceId
                if (existingDocs.Contains(raw.ExternalId))
                {
                    duplicates++;
                    continue;
                }

                // Inject the source ID before normalization
                raw.Metadata["source"] = sourceId;
                // Normalize and embed
                var doc = await normalizer.NormalizeAsync(raw, ct);

                // Store in Merkle DAG
                var cid = await merkleStore.PutBlobAsync(doc);
                hashes.Add(cid.ToString());
                newDocs++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to ingest document {ExternalId} from {SourceId}", raw.ExternalId, sourceId);
                errors++;
            }
        }

        logger.LogInformation("Ingested {New} new documents from {SourceId} ({Dup} duplicates, {Err} errors)",
            newDocs, sourceId, duplicates, errors);

        return new IngestionResult
        {
            SourceId = sourceId,
            NewDocuments = newDocs,
            DuplicatesSkipped = duplicates,
            Errors = errors,
            DocumentHashes = hashes
        };
    }

    public async Task<BiasReport> AuditBiasAsync(CancellationToken ct)
    {
        return await auditEngine.AuditAsync(ct);
    }

    [Experimental("SKEXP0001")]
    public async Task<FalsificationResult> RunFalsificationAsync(CancellationToken ct)
    {
        var biasReport = await auditEngine.AuditAsync(ct);
        var counterDataResults = new List<IngestionResult>();
        var generatedThemes = new List<string>();
        var defaultDomain = configuration.GetValue("Ingestion:DefaultDomain", "art");
        var defaultMaxResults = configuration.GetValue("Defaults:IngestionMaxResults", 50);

        if (biasReport.BiasScore > configuration.GetValue("Ingestion:BiasThreshold", 0.5f))
        {
            // For each recommended source, pull counter-data
            foreach (var sourceRef in biasReport.RecommendedSources)
            {
                var parts = sourceRef.Split(':');
                if (parts.Length < 2) continue;

                var sourceId = parts[0];
                var targetCulture = parts[1];
                var domain = parts.Length > 2 ? parts[2] : defaultDomain;

                var connector = connectors.FirstOrDefault(c => c.SourceId == sourceId);
                if (connector == null) continue;

                logger.LogInformation("Falsification: pulling counter-data from {SourceId} for culture={Culture}, domain={Domain}",
                    sourceId, targetCulture, domain);

                var rawDocs = await connector.FetchByCultureAsync(targetCulture, domain, defaultMaxResults, ct);
                var existingDocs = (await merkleStore.GetAllNodesAsync<IngestedDocument>())
                    .Where(d => d.SourceId == sourceId)
                    .ToLookup(d => d.ExternalId);

                var newDocs = 0;
                var duplicates = 0;
                var errors = 0;

                foreach (var raw in rawDocs)
                {
                    try
                    {
                        if (existingDocs.Contains(raw.ExternalId))
                        {
                            duplicates++;
                            continue;
                        }

                        raw.Metadata["source"] = sourceId;
                        var doc = await normalizer.NormalizeAsync(raw, ct);
                        await merkleStore.PutBlobAsync(doc);
                        newDocs++;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Falsification ingestion failed for {ExternalId}", raw.ExternalId);
                        errors++;
                    }
                }

                counterDataResults.Add(new IngestionResult
                {
                    SourceId = sourceId,
                    NewDocuments = newDocs,
                    DuplicatesSkipped = duplicates,
                    Errors = errors
                });
            }

            generatedThemes = biasReport.SuggestedFalsificationThemes;
        }

        var biasThreshold = configuration.GetValue("Ingestion:BiasThreshold", 0.5f);
        return new FalsificationResult
        {
            BiasDetected = biasReport.BiasScore > biasThreshold,
            BiasReport = biasReport,
            CounterDataResults = counterDataResults,
            GeneratedThemes = generatedThemes,
            Summary = biasReport.BiasScore > biasThreshold
                ? $"Falsification triggered: pulled counter-data from {counterDataResults.Count} sources, generated {generatedThemes.Count} challenge themes"
                : "No falsification needed: bias score within acceptable range"
        };
    }

    public async Task<IReadOnlyList<IngestedDocument>> GetIngestedDocumentsAsync(
        string? sourceId = null, string? culture = null, string? domain = null, CancellationToken ct = default)
    {
        var docs = await merkleStore.GetAllNodesAsync<IngestedDocument>();
        
        var filtered = docs.AsEnumerable();
        if (!string.IsNullOrEmpty(sourceId))
            filtered = filtered.Where(d => d.SourceId == sourceId);
        if (!string.IsNullOrEmpty(culture))
            filtered = filtered.Where(d => d.Culture == culture);
        if (!string.IsNullOrEmpty(domain))
            filtered = filtered.Where(d => d.Domain == domain);

        return [.. filtered];
    }
}