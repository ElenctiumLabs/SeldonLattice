namespace SeldonLattice.Infrastructure.Audit;

/// <summary>
/// Configuration options for the MEAI Audit and Quality middleware pipeline.
/// </summary>
public sealed class AuditMiddlewareOptions
{
    /// <summary>
    /// Whether to include the QualityGateDelegatingChatClient layer. Default: true.
    /// </summary>
    public bool EnableQualityGate { get; set; } = true;

    /// <summary>
    /// Jaccard bigram similarity threshold above which a completion is flagged as
    /// circular reasoning. Must be in [0.0, 1.0]. Default: 0.55.
    /// </summary>
    public float CircularReasoningThreshold { get; init; } = 0.55f;

    /// <summary>
    /// "all"          – every LlmAuditNode is persisted (default).
    /// "flagged_only" – only nodes where IsFlagged=true are persisted.
    /// </summary>
    public string AuditNodeRetentionPolicy { get; init; } = "all";
}
