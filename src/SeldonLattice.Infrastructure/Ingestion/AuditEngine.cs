using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models;
using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Infrastructure.Ingestion;

/// <summary>
/// The Audit Engine evaluates the current distribution of ingested documents
/// and synthesis nodes to detect cultural/domain bias. When bias is found,
/// it generates falsification recommendations to challenge the lattice's assumptions.
/// 
/// This is the embodiment of the Falsification principle:
/// "If the agents decide that a historical cycle exists based on a subset of data,
/// the ingestion engine is mandated to pull counter-data to attempt to falsify that cycle."
/// </summary>
public class AuditEngine(IMerkleStore merkleStore, ILogger<AuditEngine> logger, IConfiguration configuration)
{
    // Known cultural frames the system can recognize
    private static readonly string[] KnownCultures = ["western", "eastern", "african", "indigenous", "islamic", "global", "scientific"];
    
    // Known domains the system can recognize
    private static readonly string[] KnownDomains = ["art", "philosophy", "economics", "politics", "science", "history"];

    /// <summary>
    /// Audit the current state of the lattice for cultural and domain bias.
    /// </summary>
    public async Task<BiasReport> AuditAsync(CancellationToken ct)
    {
        var documents = (await merkleStore.GetAllNodesAsync<IngestedDocument>()).ToList();
        var synthesisNodes = (await merkleStore.GetAllNodesAsync<DialecticalNode>())
            .Where(n => n.NodeType == "mediator_synthesis")
            .ToList();

        var cultureDist = BuildDistribution(documents, d => d.Culture, KnownCultures);
        var domainDist = BuildDistribution(documents, d => d.Domain, KnownDomains);

        // Detect which cultures/domains are missing or under-represented
        var totalDocs = documents.Count;
        var biasScore = ComputeBiasScore(cultureDist, domainDist, totalDocs);
        var recommendedSources = GetRecommendedSources(cultureDist, domainDist);
        var suggestedThemes = GenerateFalsificationThemes(cultureDist, domainDist, synthesisNodes);

        var biasThreshold = configuration.GetValue("Ingestion:BiasThreshold", 0.5f);
        var summary = BuildSummary(cultureDist, domainDist, biasScore, totalDocs, synthesisNodes.Count, biasThreshold);

        logger.LogInformation("Audit complete: bias score={BiasScore:F2}, cultures={Cultures}, domains={Domains}",
            biasScore, 
            string.Join(",", cultureDist.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key}:{kv.Value}")),
            string.Join(",", domainDist.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key}:{kv.Value}")));

        return new BiasReport
        {
            CultureDistribution = cultureDist,
            DomainDistribution = domainDist,
            RecommendedSources = recommendedSources,
            SuggestedFalsificationThemes = suggestedThemes,
            BiasScore = biasScore,
            Summary = summary
        };
    }

    private static Dictionary<string, int> BuildDistribution(IReadOnlyList<IngestedDocument> documents, Func<IngestedDocument, string> keySelector, string[] knownKeys)
    {
        var raw = documents.GroupBy(keySelector).ToDictionary(g => g.Key, g => g.Count());
        var result = new Dictionary<string, int>();
        foreach (var key in knownKeys)
            result[key] = raw.GetValueOrDefault(key, 0);
        // Include any unknown cultures/domains
        foreach (var (k, v) in raw) result.TryAdd(k, v);
        return result;
    }

    private static float ComputeBiasScore(Dictionary<string, int> cultureDist, Dictionary<string, int> domainDist, int totalDocs)
    {
        if (totalDocs == 0) return 0f;

        // Compute entropy-based bias: if one culture dominates, bias is high
        var maxCulture = cultureDist.Values.Count != 0 ? cultureDist.Values.Max() : 0;
        var maxDomain = domainDist.Values.Count != 0 ? domainDist.Values.Max() : 0;

        var cultureBias = totalDocs > 0 ? (float)maxCulture / totalDocs : 0f;
        var domainBias = totalDocs > 0 ? (float)maxDomain / totalDocs : 0f;

        // Weighted: culture bias matters more (falsification is about cultural/epistemic diversity)
        return Math.Clamp(cultureBias * 0.7f + domainBias * 0.3f, 0f, 1f);
    }

    private static List<string> GetRecommendedSources(Dictionary<string, int> cultureDist, Dictionary<string, int> domainDist)
    {
        var sources = new List<string>();

        // Find under-represented cultures
        var minCultureCount = cultureDist.Values.DefaultIfEmpty(0).Min();
        foreach (var (culture, count) in cultureDist)
        {
            if (count <= minCultureCount && culture != "unknown" && culture != "global")
                sources.Add($"europeana:{culture}:{domainDist.OrderBy(kv => kv.Value).First().Key}");
        }

        return sources;
    }

    private static List<string> GenerateFalsificationThemes(
        Dictionary<string, int> cultureDist, 
        Dictionary<string, int> domainDist,
        IReadOnlyList<DialecticalNode> synthesisNodes)
    {
        var themes = new List<string>();

        // Find the most over-represented culture
        var dominantCulture = cultureDist.OrderByDescending(kv => kv.Value).FirstOrDefault();
        // Find the most under-represented culture (that has data)
        var underRepCulture = cultureDist.Where(kv => kv.Value > 0).OrderBy(kv => kv.Value).FirstOrDefault();

        if (dominantCulture.Key != underRepCulture.Key && underRepCulture.Key != null)
        {
            themes.Add($"Falsify the dominant {dominantCulture.Key} narrative by examining {underRepCulture.Key} perspectives on {domainDist.OrderByDescending(kv => kv.Value).First().Key}");
        }

        // If there are synthesis nodes, generate themes that challenge their assumptions
        if (synthesisNodes.Count > 0)
        {
            var recentSynthesis = synthesisNodes.OrderByDescending(n => n.Timestamp).Take(3).ToList();
            foreach (var synthesis in recentSynthesis)
            {
                var contentPreview = synthesis.Content.Length > 100 ? synthesis.Content[..100] : synthesis.Content;
                themes.Add($"Test the claim: '{contentPreview}...' against counter-evidence from {underRepCulture.Key} sources");
            }
        }

        // Generic falsification themes
        if (themes.Count == 0)
        {
            themes.Add("Examine whether Western aesthetic principles apply universally across Eastern art traditions");
            themes.Add("Test if economic cycle theories derived from Western markets predict Eastern market behavior");
        }

        return themes;
    }

    private static string BuildSummary(
        Dictionary<string, int> cultureDist, 
        Dictionary<string, int> domainDist, 
        float biasScore, 
        int totalDocs, 
        int synthesisCount,
        float biasThreshold)
    {
        var dominantCulture = cultureDist.OrderByDescending(kv => kv.Value).FirstOrDefault();
        var missingCultures = cultureDist.Where(kv => kv.Value == 0 && kv.Key != "unknown").Select(kv => kv.Key).ToList();

        var parts = new List<string>
        {
            $"Ingested documents: {totalDocs}, Synthesis nodes: {synthesisCount}",
            $"Bias score: {biasScore:F2} (0=balanced, 1=severely biased)",
            $"Dominant culture: {dominantCulture.Key} ({dominantCulture.Value} docs)"
        };

        if (missingCultures.Count > 0)
            parts.Add($"Missing cultural perspectives: {string.Join(", ", missingCultures)}");

        if (biasScore > biasThreshold)
            parts.Add("RECOMMENDED: Run falsification to pull counter-data from under-represented sources");

        return string.Join(". ", parts);
    }
}