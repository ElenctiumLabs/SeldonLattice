namespace SeldonLattice.API.Models;

/// <summary>
/// DTO for a single turn in a session's debate history.
/// </summary>
public sealed record HistoryTurn
{
    /// <summary>Sequential turn number (1-based, ordered chronologically).</summary>
    public required int TurnNumber { get; init; }
    /// <summary>The agent or module that generated this turn.</summary>
    public required string Speaker { get; init; }
    /// <summary>Dialectical role: question, thesis, antithesis, synthesis, or mediation.</summary>
    public required string Role { get; init; }
    /// <summary>The text content of this turn.</summary>
    public required string Content { get; init; }
    /// <summary>When this turn was created.</summary>
    public required DateTime Timestamp { get; init; }
    /// <summary>Overall response quality (0–100) from the quality-gate rubric. Null when the
    /// gates did not score this turn (e.g. question and synthesis nodes).</summary>
    public double? QualityScore { get; init; }
    /// <summary>Ascent level of this turn in the dialectical chain.</summary>
    public required int Level { get; init; }
    /// <summary>Ascent-quality score of a synthesis turn (0.0–1.0); 0 for non-synthesis turns.</summary>
    public float AscentScore { get; init; }
    public float? JoinMediation { get; init; }
    public float? JoinNovelty { get; init; }
    public bool IsSustained { get; init; }
    /// <summary>Negative-dialectic measure of a synthesis turn (0.0–1.0): how far it holds the
    /// contradiction open rather than resolving it; 0 for non-synthesis turns.</summary>
    public float IrreducibleTension { get; init; }

    public string? ReasoningTrace { get; init; }

    /// <summary>The sharpest live tension this synthesis does not close (moderator's own report).
    /// Null for non-synthesis turns or when the moderator did not produce one.</summary>
    public string? RemainingDissensus { get; init; }

    /// <summary>The moderator's self-assessment of how much this synthesis merely restated
    /// the question (0.0–1.0). Null for non-synthesis turns.</summary>
    public float? RepeatsPremise { get; init; }

    /// <summary>Tensions that survived mediation (mediators' own report). Null when absent.</summary>
    public string[]? UnresolvedTensions { get; init; }

    /// <summary>Intensity of the unresolved tensions (0.0–1.0). Null when unavailable.</summary>
    public float? TensionIntensity { get; init; }

    // -- Per-turn response quality metrics --------------------------------------
    // Null = the quality gates did not score this turn; 0 = measured at zero.

    /// <summary>Weighted ceremony/politeness-language score.</summary>
    public double? CeremonyScore { get; init; }

    /// <summary>Text similarity to the previous response (0.0–1.0).</summary>
    public double? SimilarityToPrevious { get; init; }

    /// <summary>Count of new arguments, counterexamples or challenging questions.</summary>
    public int? NewArgumentCount { get; init; }

    /// <summary>Word count of the reply.</summary>
    public int? WordCount { get; init; }

    /// <summary>Whether the reply contained farewell language.</summary>
    public bool? HasFarewell { get; init; }

    /// <summary>Whether the reply contained agreement markers.</summary>
    public bool? HasAgreement { get; init; }

    /// <summary>Whether the reply contained disagreement indicators.</summary>
    public bool? HasDisagreement { get; init; }

    /// <summary>Text similarity to the agent's own previous reply (0.0–1.0).</summary>
    public double? SelfSimilarity { get; init; }
}
