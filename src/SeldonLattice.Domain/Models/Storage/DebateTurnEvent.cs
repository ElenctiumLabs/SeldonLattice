using MessagePack;
using SeldonLattice.Domain.Models.Cycle;

namespace SeldonLattice.Domain.Models.Storage;

[MessagePackObject]
public record DebateTurnEvent
{
    /// <summary>
    /// Event type: "proposer_reply", "skeptic_reply", "synthesis", "mediator", "mediator_gate", "system"
    /// </summary>
    [Key(0)] public string Type { get; init; } = string.Empty;

    /// <summary>
    /// The debate turn number (0-indexed). -1 for synthesis/system events.
    /// </summary>
    [Key(1)] public int Turn { get; init; }

    /// <summary>
    /// The text content of this turn.
    /// </summary>
    [Key(2)] public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Only set on "synthesis" events - the cleaned metaphor.
    /// </summary>
    [Key(3)] public string? Metaphor { get; init; }

    /// <summary>
    /// Only set on "synthesis" events - the next theme to explore.
    /// </summary>
    [Key(4)] public string? NextTheme { get; init; }

    /// <summary>
    /// The current ascent level.
    /// </summary>
    [Key(5)] public int Level { get; init; }

    // --- Quality metrics (populated when quality gates are enabled) ---

    /// <summary>
    /// Weighted ceremony language score for this response.
    /// Higher values indicate excessive politeness/ceremonial filler.
    /// </summary>
    [Key(6)] public double CeremonyScore { get; init; }

    /// <summary>
    /// Text similarity to the previous response (0.0-1.0).
    /// High values indicate repetition.
    /// </summary>
    [Key(7)] public double SimilarityToPrevious { get; init; }

    /// <summary>
    /// Count of new arguments, counterexamples, or challenging questions.
    /// </summary>
    [Key(8)] public int NewArgumentCount { get; init; }

    /// <summary>
    /// Overall quality score (0-100) based on the rubric:
    /// Content (40pts) + Tone (30pts) + Progression (20pts) + Length (10pts)
    /// </summary>
    [Key(9)] public double QualityScore { get; init; }

    /// <summary>
    /// Word count of the response.
    /// </summary>
    [Key(10)] public int WordCount { get; init; }

    /// <summary>
    /// Whether the response contains farewell language.
    /// </summary>
    [Key(11)] public bool HasFarewell { get; init; }

    /// <summary>
    /// Whether the response contains agreement markers.
    /// </summary>
    [Key(12)] public bool HasAgreement { get; init; }

    /// <summary>
    /// Whether the response contains disagreement indicators.
    /// </summary>
    [Key(13)] public bool HasDisagreement { get; init; }

    /// <summary>
    /// If set, the quality gates system has terminated the dialogue.
    /// Contains the exit reason.
    /// </summary>
    [Key(14)] public string? ExitReason { get; init; }

    /// <summary>
    /// If set on a "mediator" event, indicates which stalemate signal triggered the interjection.
    /// </summary>
    [Key(15)] public StalemateSignal? TriggeringSignal { get; init; }

    /// <summary>
    /// Only set on "synthesis" events - one plain sentence stating how NextTheme
    /// generalizes the session's root question (see DialogueLevelResult.ConnectiveSentence).
    /// </summary>
    [Key(16)] public string? ConnectiveSentence { get; init; }

    /// <summary>
    /// Only set on "synthesis" events - whether the ascension gate accepted
    /// NextTheme as a genuine generalization of the root question. On "mediator_gate"
    /// events (emitted once per rejected candidate before a retry), Content carries the
    /// gate's rejection reasoning instead.
    /// </summary>
    [Key(17)] public bool? AscensionValidated { get; init; }

    /// <summary>
    /// Only set on "synthesis" events - one plain sentence stating the actual
    /// philosophical claim being synthesized in the metaphor, as extracted by the Mediator
    /// from the THESIS line of the structured metaphor prompt. Surface this directly in
    /// the UI so the user gets the actual philosophical thesis rather than just an
    /// allegorical story to interpret.
    /// </summary>
    [Key(18)] public string? SynthesisThesis { get; init; }

    /// <summary>
    /// Who spoke this turn: the speaking agent's role key (e.g. "proposer", "skeptic", "mediator")
    /// </summary>
    [Key(19)] public string SpeakerId { get; init; } = string.Empty;

    /// <summary>
    /// The UTC timestamp for when this event was generated.
    /// </summary>
    [Key(20)] public DateTime Timestamp { get; init; }

    /// <summary>
    /// Dialectical role in the ascending helix: Thesis, Antithesis, or Synthesis.
    /// </summary>
    [Key(21)] public DialecticRole DialecticRole { get; init; } = DialecticRole.Thesis;

    /// <summary>
    /// The dialectical phase for this turn (emergence, growth, saturation, crisis, renewal).
    /// Computed by CyclePhaseDetector based on distance/variability windows.
    /// </summary>
    [Key(22)] public string? Phase { get; init; }

    /// <summary>
    /// Indicates whether this debate turn event was flagged for prompt injection or threat activation.
    /// </summary>
    [Key(23)] public bool IsSecurityFlagged { get; init; }

    /// <summary>
    /// Threat classification or reason if this event was security flagged.
    /// </summary>
    [Key(24)] public string? SecurityThreatReason { get; init; }

    /// <summary>
    /// Only set on "synthesis" events - true when the ascension gate rejected
    /// every candidate theme and confidence fell below the sustained threshold, so the
    /// dialectic stayed on the current theme rather than forcing a low-confidence ascent.
    /// When true, NextTheme is empty and the current theme is carried forward.
    /// </summary>
    [Key(25)] public bool? IsSustained { get; init; }

    /// <summary>
    /// Only set on "synthesis" events - the Ascent-quality score (0.0-1.0):
    /// rewards reference coverage and novelty against prior levels, penalizes
    /// restating a pole. Surfaced in the UI as a diagnostic; low scores feed the
    /// downstream analysis.
    /// </summary>
    [Key(26)] public double? AscentScore { get; init; }

    /// <summary>
    /// Only set on "synthesis" events - the negative-dialectic measure (0.0-1.0): how far the
    /// synthesis holds the thesis/antithesis contradiction open rather than resolving it.
    /// High is a distinct success state, not a low-ascent failure. Reported on every synthesis
    /// alongside <see cref="AscentScore"/>.
    /// </summary>
    [Key(27)] public double? IrreducibleTension { get; init; }

    /// <summary>
    /// Only set on "synthesis" events - the moderator's own report of the sharpest live
    /// tension this synthesis does not close. Carried on the stream so the Inspector can
    /// show it live; without it the value existed on the stored node but reached the UI
    /// only after a reload, and the live panel showed nothing.
    /// </summary>
    [Key(28)] public string? RemainingDissensus { get; init; }

    /// <summary>
    /// Only set on "synthesis" events - the moderator's self-assessment (0.0-1.0) of how
    /// much the synthesis merely restates the question. See <see cref="RemainingDissensus"/>.
    /// </summary>
    [Key(29)] public double? RepeatsPremise { get; init; }

    // --- Token usage (populated per-turn for trace cost attribution) ---

    /// <summary>
    /// Input tokens consumed by the LLM call that produced this turn.
    /// </summary>
    [Key(30)] public long? InputTokens { get; init; }

    /// <summary>
    /// Output tokens produced by the LLM call that generated this turn.
    /// </summary>
    [Key(31)] public long? OutputTokens { get; init; }

    /// <summary>
    /// Model identifier that produced this turn (e.g. "Qwen3-30B-A3B-Instruct-2507-Q4_K_M").
    /// </summary>
    [Key(32)] public string? ModelId { get; init; }

    /// <summary>
    /// Only set on "synthesis" events when <see cref="IsSustained"/> is true - which of the two
    /// paths sustained the rung. "gate_exhausted" means the gate could not validate any candidate
    /// after its retries; "exhaustion_confirmed" means the moderator declined to propose and the
    /// gate confirmed the decline. The two mean different things and must be watched separately,
    /// so they are carried on the event rather than folded into a single boolean.
    /// </summary>
    [Key(33)] public string? SustainedReason { get; init; }
}
