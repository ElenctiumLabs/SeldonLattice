using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.API.Models;

/// <summary>
/// API response DTO for a dialectical node.
/// This projection avoids leaking the full domain model (including embeddings) over the wire.
/// </summary>
public sealed record Node
{
    /// <summary>Content identifier (CID) of the node.</summary>
    public required string Cid { get; init; }
    /// <summary>Type of node: "question", "proposer_reply", "skeptic_reply", "mediator_synthesis".</summary>
    public required string NodeType { get; init; }
    /// <summary>Ascent level this node belongs to (0 = root question).</summary>
    public required int Level { get; init; }
    /// <summary>Single parent hash for linear chains (maybe null for root nodes).</summary>
    public string? ParentHash { get; init; }
    /// <summary>Multiple parent hashes for synthesis nodes that merge responses.</summary>
    public string[]? ParentHashes { get; init; }
    /// <summary>Text content of the node.</summary>
    public required string Content { get; init; }
    /// <summary>Dialectical cycle phase: emergence, growth, crisis, saturation, renewal.</summary>
    public required string CyclePhase { get; init; }
    /// <summary>How much the semantic content is changing between turns (0-1).</summary>
    public required float VariabilityScore { get; init; }
    /// <summary>Resonance with previously discovered insights (0-1).</summary>
    public required float ResonanceScore { get; init; }
    /// <summary>Whether this node represents a secondary dialectical center.</summary>
    public required bool IsSecondaryCenter { get; init; }
    /// <summary>Shift from parent center (0-1).</summary>
    public required float CenterShiftScore { get; init; }
    /// <summary>Session this node belongs to.</summary>
    public required string SessionId { get; init; }
    /// <summary>Agent who generated this node (e.g. "proposer", "skeptic", "mediator").</summary>
    public required string? SpeakerId { get; init; }
    /// <summary>Moderator selection confidence for synthesis nodes (0-1).</summary>
    public required float ModeratorSelectionConfidence { get; init; }
    /// <summary>When this node was created.</summary>
    public required DateTime Timestamp { get; init; }
    /// <summary>Hash of the primary pattern this node relates to.</summary>
    public string? ParentPatternHash { get; init; }

    /// <summary>Authority references (citations) supporting this node, if any.</summary>
    public Citation[]? AuthorityReferences { get; init; }

    /// <summary>True when a synthesis node was sustained (no forced ascent; current theme carried forward).</summary>
    public bool IsSustained { get; init; }

    /// <summary>Ascent-quality score computed for synthesis nodes (restates-thesis penalized, novelty/reference rewarded).</summary>
    public float AscentScore { get; init; }
    /// <summary>Concept-lattice distance from the thesis/antithesis midpoint.</summary>
    public float? JoinMediation { get; init; }
    /// <summary>Concept-lattice novelty relative to both debate poles.</summary>
    public float? JoinNovelty { get; init; }

    /// <summary>Negative-dialectic measure for synthesis nodes: how far the synthesis holds the
    /// contradiction open rather than resolving it (0-1). Independent of <see cref="AscentScore"/>.</summary>
    public float IrreducibleTension { get; init; }

    public string? ReasoningTrace { get; init; }

    /// <summary>The sharpest live tension this synthesis does not close (moderator's own report).
    /// Null for non-synthesis nodes or when the moderator did not produce one.</summary>
    public string? RemainingDissensus { get; init; }

    /// <summary>The moderator's self-assessment of how much this synthesis merely restated
    /// the question (0.0–1.0). Null for non-synthesis nodes.</summary>
    public float? RepeatsPremise { get; init; }

    /// <summary>Tensions that survived mediation (mediators' own report). Null when absent.</summary>
    public string[]? UnresolvedTensions { get; init; }

    /// <summary>Intensity of the unresolved tensions (0.0–1.0). Null when unavailable.</summary>
    public float? TensionIntensity { get; init; }

    // -- Per-turn response quality metrics --------------------------------------
    // Null means the quality gates did not score this node; 0 is a genuine measured
    // zero. Keeping them nullable is what lets downstream analytics tell "unmeasured"
    // apart from "measured and scored zero".

    /// <summary>Overall response quality (0–100) from the quality-gate rubric.</summary>
    public double? QualityScore { get; init; }

    /// <summary>Weighted ceremony/politeness-language score. Higher = more ceremonial filler.</summary>
    public double? CeremonyScore { get; init; }

    /// <summary>Text similarity to the previous response (0.0–1.0). High indicates repetition.</summary>
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
