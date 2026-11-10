using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Domain.Utilities;

namespace SeldonLattice.Infrastructure.Services;

/// <summary>
/// Citation provider that searches ingested documents stored in the Merkle store
/// using embedding similarity.
/// </summary>
public class MerkleCitationProvider(
    IMerkleStore merkleStore,
    Kernel kernel,
    ILogger<MerkleCitationProvider> logger)
    : ICitationProvider
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingService = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

    // Resolve the embedding service from the Kernel so the provider does not require the embedding
    // service to be registered in the application's root IServiceCollection.

    public async Task<IReadOnlyList<Citation>> GetTopCitationsAsync(string query, int topN = 5, CancellationToken ct = default)
    {
        try
        {
            var qEmbMem = (await _embeddingService.GenerateAsync([query], cancellationToken: ct))[0].Vector;
            var qEmb = qEmbMem.ToArray();

            var docs = (await merkleStore.GetAllNodesAsync<IngestedDocument>()).Where(d => d.Embedding is
            {
                Length: > 0
            }).ToList();
            if (docs.Count == 0) return [];

            var scored = new List<(IngestedDocument doc, double score)>();
            foreach (var d in docs)
            {
                try
                {
                    var sim = SimdVectorUtility.CosineSimilarity(d.Embedding, qEmb);
                    scored.Add((d, sim));
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to score doc {ExternalId}", d.ExternalId);
                }
            }

            var top = scored.OrderByDescending(s => s.score).Take(topN).ToList();

            var results = top.Select(t => new Citation
            {
                Id = t.doc.ExternalId,
                Title = !string.IsNullOrEmpty(t.doc.Title) ? t.doc.Title : t.doc.ExternalId,
                Url = t.doc.Metadata.GetValueOrDefault("url", string.Empty),
                SourceId = t.doc.SourceId,
                Confidence = Math.Clamp(t.score, 0.0, 1.0)
            }).ToList();

            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Citation provider failed for query: {Query}", query);
            return [];
        }
    }
}