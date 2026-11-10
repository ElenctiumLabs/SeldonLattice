using MessagePack;
using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.Domain.Models.Storage;

/// <summary>
/// Represents a node in the dialectical dialogue graph.
/// Each node captures a moment of philosophical exchange,
/// with semantic embeddings for measuring convergence and resonance.
/// </summary>
[MessagePackObject]
public record DialecticalNode : IMerkleNode
{
    /// <summary>
    /// Type of node: "question", "proposer_reply", "skeptic_reply", or "mediator_synthesis".
    /// </summary>
    [Key(0)] public string NodeType { get; init; } = string.Empty;

    /// <summary>
    /// The ascent level this node belongs to (0 = root question, higher = deeper inquiry).
    /// </summary>
    [Key(1)] public int Level { get; init; }

    /// <summary>
    /// Single parent hash for simple linear chains (deprecated, use ParentHashes).
    /// </summary>
    [Key(2)] public string? ParentHash { get; init; }

    /// <summary>
    /// Multiple parent hashes for synthesis nodes that merge proposer and skeptic responses.
    /// </summary>
    [Key(3)] public string[]? ParentHashes { get; init; }

    /// <summary>
    /// The actual text content of the node (question, reply, or metaphor).
    /// </summary>
    [Key(4)] public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Embedding vector for semantic similarity comparisons.
    /// Generated from the Content using the configured embedding service.
    /// </summary>
    [Key(5)] public float[] Embedding { get; init; } = [];

    /// <summary>
    /// When this node was created.
    /// </summary>
    [Key(6)] public DateTime Timestamp { get; init; }

    /// <summary>
    /// The dialectical phase of this node: emergence, growth, crisis, saturation, or renewal.
    /// </summary>
    [Key(7)] public string CyclePhase { get; init; } = "emergence";

    /// <summary>
    /// Measure of how much the semantic content is changing between turns.
    /// High variability indicates active exploration; low suggests convergence.
    /// </summary>
    [Key(8)] public float VariabilityScore { get; init; }

    /// <summary>
    /// How similar this node's embedding is to recent synthesis nodes.
    /// Measures resonance with previously discovered insights.
    /// </summary>
    [Key(9)] public float ResonanceScore { get; init; }

    /// <summary>
    /// Hash of the primary pattern/center this node relates to.
    /// </summary>
    [Key(10)] public string? ParentPatternHash { get; init; }

    /// <summary>
    /// Whether this node represents a secondary dialectical center.
    /// </summary>
    [Key(11)] public bool IsSecondaryCenter { get; init; }

    /// <summary>
    /// Score indicating how much this node shifts away from its parent center.
    /// </summary>
    [Key(12)] public float CenterShiftScore { get; init; }

    /// <summary>
    /// The session this node belongs to, linking nodes into dialogue threads.
    /// </summary>
    [Key(13)] public string SessionId { get; init; } = string.Empty;

    /// <summary>
    /// Who generated this node (e.g. "proposer", "skeptic", "mediator").
    /// Empty for question nodes.
    /// </summary>
    [Key(14)] public string? SpeakerId { get; init; } = string.Empty;

    /// <summary>
    /// For synthesis nodes produced by a module with a dynamic moderator selector
    /// (a moderator-selection scenario), this is the [0.0, 1.0]
    /// confidence of that selection - the gap between the winning moderator's score and
    /// the runner-up's. Lower values mean the moderator was a near-tie or a defaulted
    /// low-confidence pick. Callers (frontend InspectorPanel, downstream FASTER queries)
    /// surface this so users can see when a synthesis was made by a clearly-fitting
    /// moderator vs. an arbitrary tie-break. Always 0 for non-synthesis nodes and for
    /// modules that use a fixed moderator (the philosophy module's Mediator).
    /// </summary>
    [Key(15)] public float ModeratorSelectionConfidence { get; init; }

    /// <summary>
    /// Authority references (citations) used to support synthesis/conclusions.
    /// Populated by modules that integrate ICitationProvider (optional).
    /// </summary>
    [Key(16)] public Citation[]? AuthorityReferences { get; init; }

    /// <summary>
    /// RESERVED FOR FUTURE USE (J-Space): top-k concept labels from a J-Space snapshot.
    /// No producer currently populates this - J-Space capture is planned as an
    /// IJSpaceService (see docs/jlens/). Do not treat as live data.
    /// </summary>
    [Key(17)] public string[]? JSpaceConcepts { get; init; }

    /// <summary>
    /// RESERVED FOR FUTURE USE (J-Space): normalised influence weights for J-Space concepts.
    /// See <see cref="JSpaceConcepts"/>.
    /// </summary>
    [Key(18)] public float[]? JSpaceWeights { get; init; }

    /// <summary>
    /// RESERVED FOR FUTURE USE (J-Space): concepts activated internally but withheld from Content.
    /// See <see cref="JSpaceConcepts"/>.
    /// </summary>
    [Key(19)] public string[]? SilentThoughts { get; init; }

    /// <summary>
    /// RESERVED FOR FUTURE USE (J-Space): SHA-256 hash of the serialised JSpaceSnapshot.
    /// See <see cref="JSpaceConcepts"/>.
    /// </summary>
    [Key(20)] public string? JSpaceHash { get; init; }

    /// <summary>
    /// RESERVED FOR FUTURE USE (J-Space): continuous reasoning-depth metric.
    /// See <see cref="JSpaceConcepts"/>.
    /// </summary>
    [Key(21)] public float JDepthScore { get; init; }

    /// <summary>
    /// Indicates whether this node was flagged by the Prompt Injection Security Guard.
    /// </summary>
    [Key(22)] public bool IsSecurityFlagged { get; init; }

    /// <summary>
    /// Reason/vector type if this node was flagged for security concerns.
    /// </summary>
    [Key(23)] public string? SecurityThreatReason { get; init; }

    /// <summary>
    /// For synthesis nodes, true when the ascension gate rejected every
    /// candidate next theme (and any moderator-selection confidence fell below
    /// DialecticsOptions.SustainedConfidenceThreshold), so the dialectic carried the
    /// current theme forward instead of forcing a low-confidence ascent. When true,
    /// consumers should surface it for human review.
    /// </summary>
    [Key(24)] public bool IsSustained { get; init; }

    /// <summary>
    /// Ascent-quality score of the accepted synthesis in [0,1] (Reasoning Integrity
    /// Layer): combines how well the synthesis mediates the thesis/antithesis poles and
    /// how far it advances beyond both. Recorded for downstream analysis;
    /// 0 when unavailable or not yet scored.
    /// </summary>
    [Key(25)] public float AscentScore { get; init; }

    /// <summary>
    /// S1 concept-lattice telemetry (optional): Euclidean deviation of the synthesis intent
    /// (join) from the thesis/antithesis midpoint, <c>||join ⇒ mid||</c>. Null when unavailable
    /// (e.g. pre-S1 history or embedding provider failure).
    /// </summary>
    [Key(26)] public float? JoinMediation { get; init; }

    /// <summary>
    /// S1 concept-lattice telemetry (optional): <c>cos(join, thesis) + cos(join, antithesis)</c>,
    /// the join-against-poles novelty term of the ascent objective. Null when unavailable.
    /// </summary>
    [Key(27)] public float? JoinNovelty { get; init; }

    /// <summary>
    /// Negative-dialectic measure (optional): how far the synthesis holds the thesis/antithesis
    /// contradiction open rather than resolving it, <c>betweenness × (1 - max pole similarity)</c>.
    /// High = grounded in the disagreement and captured by neither pole. Independent of
    /// <see cref="AscentScore"/>; null when unavailable (pre-metric history or embedding failure).
    /// </summary>
    [Key(28)] public float? IrreducibleTension { get; init; }

    /// <summary>
    /// Zero-based turn index within the level, for nodes written per-turn.
    ///
    /// Until this was added, the debate loop overwrote its per-agent "last text" each turn and
    /// stored only the final reply, so a 4-turn level kept 2 of 8 replies and the intervening
    /// exchange was lost. Every turn is now persisted with its index so the round-by-round
    /// argument is recoverable. Null on nodes that are not tied to a turn (the level's question,
    /// the synthesis).
    /// </summary>
    [Key(29)] public int? Turn { get; init; }

    /// <summary>
    /// The model's reasoning/thinking trace for this node - its deliberation record:
    /// what it considered, planned, and rejected before producing the answer.
    /// Populated from <c>reasoning_content</c> returned by reasoning-capable models (e.g. Gemma)
    /// when <see cref="SeldonLattice.Domain.Options.LlmOptions.PersistReasoningTrace"/> is true.
    /// Null when thinking is disabled, the trace is empty, or the option is off.
    /// </summary>
    [Key(30)] public string? ReasoningTrace { get; init; }

    /// <summary>
    /// The sharpest live tension this synthesis does not close - the moderator's own
    /// report of what remained unresolved. Null when absent or when the moderator did
    /// not produce one.
    /// </summary>
    [Key(31)] public string? RemainingDissensus { get; init; }

    /// <summary>
    /// The moderator's self-assessment of how much this synthesis merely restated
    /// the question. Null when absent.
    /// </summary>
    [Key(32)] public float? RepeatsPremise { get; init; }

    /// <summary>
    /// Tensions that survived mediation - the mediators' own report of what the
    /// dialecticians could not reconcile. Null when no mediation ran or when no
    /// tensions were reported.
    /// </summary>
    [Key(33)] public string[]? UnresolvedTensions { get; init; }

    /// <summary>
    /// Intensity of the unresolved tensions, as reported by mediators.
    /// Null when unavailable.
    /// </summary>
    [Key(34)] public float? TensionIntensity { get; init; }

    //  Per-turn response quality metrics (Keys 35-43) 
    //
    // These are produced by DialogueQualityGates.AnalyzeResponse for every dialectician
    // reply and, until now, existed only on the in-flight DebateTurnEvent and in the
    // browser's localStorage. Nothing durable held them, so a restart lost every
    // non-synthesis node's metrics and no analytics could run over stored history.
    //
    // They are nullable deliberately. A non-nullable score would make "the gates never
    // ran for this node" indistinguishable from "the gates ran and awarded zero", and a
    // genuine 0.0 (a maximally repetitive reply) is a real and important observation.
    // Null means not measured; 0 means measured at zero.
    //
    // Note this widens the positional MessagePack array, which changes every node's CID.
    // Stored data written before this change must be wiped rather than migrated: parent
    // hashes reference the old CIDs and the ML-DSA signatures are bound to the old bytes.

    /// <summary>Overall response quality (0-100) from the quality-gate rubric.</summary>
    [Key(35)] public double? QualityScore { get; init; }

    /// <summary>Weighted ceremony/politeness-language score. Higher = more ceremonial filler.</summary>
    [Key(36)] public double? CeremonyScore { get; init; }

    /// <summary>Text similarity to the previous response. High indicates repetition.</summary>
    [Key(37)] public double? SimilarityToPrevious { get; init; }

    /// <summary>Count of new arguments, counterexamples or challenging questions in the reply.</summary>
    [Key(38)] public int? NewArgumentCount { get; init; }

    /// <summary>Word count of the reply.</summary>
    [Key(39)] public int? WordCount { get; init; }

    /// <summary>Whether the reply contained farewell language.</summary>
    [Key(40)] public bool? HasFarewell { get; init; }

    /// <summary>Whether the reply contained agreement markers.</summary>
    [Key(41)] public bool? HasAgreement { get; init; }

    /// <summary>Whether the reply contained disagreement indicators.</summary>
    [Key(42)] public bool? HasDisagreement { get; init; }

    /// <summary>Text similarity to the agent's own previous reply. Feeds the self-repetition penalty.</summary>
    [Key(43)] public double? SelfSimilarity { get; init; }

    /// <summary>
    /// Computes the CID by hashing the serialized node.
    /// </summary>
    public Cid GetCid() => Cid.Compute(Serialize());

    /// <summary>
    /// Serializes using MessagePack for efficient binary storage.
    /// </summary>
    public byte[] Serialize() => MessagePackSerializer.Serialize(this);
}
