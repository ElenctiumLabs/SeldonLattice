using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Domain.Options;

namespace SeldonLattice.Application.Memory;

/// <summary>
/// A single claim recalled from the cross-session memory substrate, together with the
/// similarity that made it retrievable and its provenance (session/level the claim was
/// adjudicated in). Consumers (prompt-grounding, diagnostics) should treat the claim as
/// provisional: confidence is producer-reported and must never exceed the strength of its
/// support set.
/// </summary>
public sealed record RetrievedClaim(
    int Level,
    string SessionId,
    string Claim,
    float Confidence,
    float Similarity,
    string? FalsifiedBy,
    string[] SupportedBy);

/// <summary>Tunable knobs for a single <see cref="IClaimMemoryRetriever"/> query.</summary>
public sealed record ClaimMemoryQueryOptions
{
    /// <summary>Maximum number of claims to return (default 5).</summary>
    public int MaxResults { get; init; } = 5;

    /// <summary>Minimum token-overlap similarity for a claim to be recalled (default 0.20).</summary>
    public float MinSimilarity { get; init; } = 0.20f;

    /// <summary>When true (default), claims already produced in the querying session are
    /// excluded so the memory augments, rather than repeats, the current debate.</summary>
    public bool ExcludeSameSession { get; init; } = true;

    /// <summary>Scoring mode (default TokenOverlap). Embedding costs one batched embedding
    /// call per recall but captures semantic equivalence beyond literal vocabulary.</summary>
    public ClaimMemoryMode Mode { get; init; } = ClaimMemoryMode.TokenOverlap;
}

/// <summary>
/// Retrieves claims stored by prior sessions that bear on a new root question - the
/// "root-anchored RAG" over <see cref="ArgumentClaimNode"/> (Reasoning Integrity Layer,
/// Phase 1 row 3). Supports two scoring modes, resolved from <see cref="DialecticsOptions"/>
/// at call time:
/// TokenOverlap - deterministic, embedding-free Jaccard similarity; minimum-similarity
/// filtered and ranked by similarity then confidence. Embedding - cosine similarity via
/// the provider's embedding service over the whole session-excluded set, capturing
/// semantic equivalence that literal token overlap misses; the token-overlap score is
/// still computed (for diagnostics) but only the cosine scores are filtered and ranked.
/// </summary>
public interface IClaimMemoryRetriever
{
    Task<IReadOnlyList<RetrievedClaim>> RetrieveAsync(
        string query,
        string excludeSessionId,
        ClaimMemoryQueryOptions? options = null,
        CancellationToken ct = default);
}

public sealed class ClaimMemoryRetriever(IMerkleStore merkleStore, Kernel kernel) : IClaimMemoryRetriever
{
    public async Task<IReadOnlyList<RetrievedClaim>> RetrieveAsync(
        string query,
        string excludeSessionId,
        ClaimMemoryQueryOptions? options = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var opts = options ?? new ClaimMemoryQueryOptions();
        var mode = opts.Mode;
        var queryTokens = Tokenize(query);
        if (queryTokens.Length == 0)
            return [];

        var claims = await merkleStore.GetAllNodesAsync<ArgumentClaimNode>();
        ct.ThrowIfCancellationRequested();

        // Collect the session-excluded, non-empty claim candidates.
        var candidates = new List<(ArgumentClaimNode Node, float JaccardScore)>();
        foreach (var claim in claims)
        {
            ct.ThrowIfCancellationRequested();
            if (opts.ExcludeSameSession && claim.SessionId == excludeSessionId)
                continue;
            if (string.IsNullOrWhiteSpace(claim.ClaimText))
                continue;

            var jaccard = Jaccard(queryTokens, Tokenize(claim.ClaimText));
            if (mode == ClaimMemoryMode.TokenOverlap && jaccard < opts.MinSimilarity)
                continue;

            candidates.Add((claim, jaccard));
        }

        if (candidates.Count == 0)
            return [];

        IReadOnlyList<RetrievedClaim> hits;

        // Embedding mode cosine-ranks every candidate so claims that share no literal
        // vocabulary with the query (but are semantically equivalent) are still recalled;
        // opts.MinSimilarity is applied to the cosine scores. The extra embedding cost is
        // bounded in practice because recall is memoized per session by the caller.
        if (mode == ClaimMemoryMode.Embedding)
            hits = await RankByEmbeddingAsync(query, candidates, opts, ct);
        else
            hits = RankByJaccard(candidates, opts);

        return hits;
    }

    private static IReadOnlyList<RetrievedClaim> RankByJaccard(
        List<(ArgumentClaimNode Node, float JaccardScore)> candidates,
        ClaimMemoryQueryOptions opts)
    {
        return
        [
            .. candidates
                .OrderByDescending(c => c.JaccardScore)
                .ThenByDescending(c => c.Node.Confidence)
                .ThenBy(c => c.Node.SessionId)
                .ThenBy(c => c.Node.Level)
                .Take(opts.MaxResults)
                .Select(c => new RetrievedClaim(
                    c.Node.Level,
                    c.Node.SessionId,
                    c.Node.ClaimText,
                    c.Node.Confidence,
                    c.JaccardScore,
                    c.Node.FalsifiedBy,
                    c.Node.SupportedBy))
        ];
    }

	private async Task<IReadOnlyList<RetrievedClaim>> RankByEmbeddingAsync(
		string query,
		List<(ArgumentClaimNode Node, float JaccardScore)> candidates,
		ClaimMemoryQueryOptions opts,
		CancellationToken ct)
	{
		var generator = kernel.GetRequiredService<
			IEmbeddingGenerator<string, Embedding<float>>>();

		// Embed the query once.
		var queryEmbedding = await generator.GenerateAsync(query, cancellationToken: ct);
		var queryVector = queryEmbedding.Vector.ToArray();

		// Batch-embed candidate claim texts.
		var claimTexts = candidates.Select(c => c.Node.ClaimText).ToList();
		var claimEmbeddings = await generator.GenerateAsync(
			claimTexts,
			cancellationToken: ct);

		var scored = new List<(ArgumentClaimNode Node, float CosineScore)>(candidates.Count);

		for (var i = 0; i < candidates.Count; i++)
		{
			var claimVector = claimEmbeddings[i].Vector.ToArray();
			var cosine = Cosine(queryVector, claimVector);

			if (cosine < opts.MinSimilarity)
				continue;

			scored.Add((candidates[i].Node, cosine));
		}

		return
		[
			.. scored
			.OrderByDescending(c => c.CosineScore)
			.ThenByDescending(c => c.Node.Confidence)
			.ThenBy(c => c.Node.SessionId)
			.ThenBy(c => c.Node.Level)
			.Take(opts.MaxResults)
			.Select(c => new RetrievedClaim(
				c.Node.Level,
				c.Node.SessionId,
				c.Node.ClaimText,
				c.Node.Confidence,
				c.CosineScore,
				c.Node.FalsifiedBy,
				c.Node.SupportedBy))
		];
	}

	/// <summary>Cosine similarity between two vectors of equal length. Returns 0 for
	/// zero-norm or mismatched inputs.</summary>
	public static float Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || a.Length != b.Length)
            return 0f;

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot   += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom == 0 ? 0f : (float)(dot / denom);
    }

    /// <summary>Lowercased alphanumeric word tokens of length &gt; 2, cardinality-bounded
    /// so pathological texts cannot blow up the Jaccard cost.</summary>
    public static string[] Tokenize(string text)
    {
        const int maxTokens = 256;
        var result = new List<string>(maxTokens);
        foreach (var raw in text.ToLowerInvariant().Split(NonWordChars, StringSplitOptions.RemoveEmptyEntries))
        {
            if (result.Count >= maxTokens) break;
            if (raw.Length > 2) result.Add(raw);
        }
        return [.. result.Distinct().Take(maxTokens)];
    }

    public static float Jaccard(string[] a, string[] b)
    {
        if (a.Length == 0 || b.Length == 0) return 0f;
        var bSet = new HashSet<string>(b);
        var intersection = 0;
        foreach (var token in a)
        {
            if (bSet.Contains(token)) intersection++;
        }
        var union = (new HashSet<string>(a)).Count + bSet.Count - intersection;
        return union == 0 ? 0f : (float)intersection / union;
    }

    private static readonly char[] NonWordChars =
        [.. Enumerable.Range(0, 128).Select(i => (char)i).Where(c => !char.IsLetterOrDigit(c))];
}
