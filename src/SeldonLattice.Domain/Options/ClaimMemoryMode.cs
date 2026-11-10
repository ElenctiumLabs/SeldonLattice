namespace SeldonLattice.Domain.Options;

/// <summary>
/// Determines how the cross-session claim memory substrate scores candidate claims
/// against a new root question. TokenOverlap is deterministic and embedding-free;
/// Embedding uses the provider's embedding service for deeper semantic matching
/// at the cost of an additional LLM-provider call per recall.
/// </summary>
public enum ClaimMemoryMode
{
    /// <summary>
    /// Token-overlap (Jaccard) similarity. Deterministic, offline-testable,
    /// no embedding dependency. Good default for most sessions.
    /// </summary>
    TokenOverlap,

    /// <summary>
    /// Embedding cosine similarity via the provider's embedding service.
    /// Captures semantic equivalence that token overlap misses (e.g. "basic income"
    /// ≈ "citizen's dividend"). Requires an embedding service registered in DI.
    /// </summary>
    Embedding
}
