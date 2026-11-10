using MessagePack;

namespace SeldonLattice.Domain.Models.Storage;

/// <summary>
/// Result of completing a single level of the dialectical ascent.
/// Contains the synthesized metaphor and the next theme to explore.
/// </summary>
[MessagePackObject]
public record DialogueLevelResult
{
    /// <summary>
    /// An empty result used when no synthesis was produced.
    /// </summary>
    public static DialogueLevelResult Empty => new DialogueLevelResult
    {
        Level = 0,
        SynthesisHash = string.Empty,
        Metaphor = string.Empty,
        NextTheme = string.Empty,
        ConnectiveSentence = string.Empty,
        AscensionValidated = false,
        AscensionAttempts = 0,
        SynthesisThesis = string.Empty
    };

    /// <summary>
    /// The ascent level that was completed.
    /// </summary>
    [Key(0)] public int Level { get; init; }

    /// <summary>
    /// CID hash of the synthesized dialogue node.
    /// </summary>
    [Key(1)] public string SynthesisHash { get; init; } = string.Empty;

    /// <summary>
    /// The synthesized metaphor weaving together the two agent perspectives.
    /// </summary>
    [Key(2)] public string Metaphor { get; init; } = string.Empty;

    /// <summary>
    /// The next concrete question extracted from the metaphor for the next ascent level.
    /// </summary>
    [Key(3)] public string NextTheme { get; init; } = string.Empty;

    /// <summary>
    /// One plain sentence, produced by the Mediator, stating how NextTheme is a more general
    /// version of the session's root question - the mandatory "same question, higher
    /// rung" link that keeps the ascent a Diotima's ladder rather than a chain of
    /// metaphors drifting through their own vocabulary.
    /// </summary>
    [Key(4)] public string ConnectiveSentence { get; init; } = string.Empty;

    /// <summary>
    /// Whether the ascension gate accepted NextTheme as a genuine
    /// generalization of the session's root question. False means every retry within
    /// DialecticsOptions.MaxAscensionGateRetries was rejected and the last candidate was
    /// used anyway to avoid stalling the ascent indefinitely - downstream consumers may
    /// want to flag this level for human review.
    /// </summary>
    [Key(5)] public bool AscensionValidated { get; init; }

    /// <summary>
    /// How many candidate themes the ascension gate evaluated for this level
    /// (1 = accepted on the first try).
    /// </summary>
    [Key(6)] public int AscensionAttempts { get; init; }

    /// <summary>
    /// One plain sentence, produced by the Mediator, stating the actual philosophical claim
    /// being synthesized in the metaphor - the specific Hegelian aufhebung, distinction,
    /// or reframing that resolves the abstract-practical tension. Surface this field
    /// directly in the UI/API so the user gets the actual philosophical thesis, not just
    /// an atmospheric story to interpret themselves.
    /// </summary>
    [Key(7)] public string SynthesisThesis { get; init; } = string.Empty;

    /// <summary>
    /// When the module uses a dynamic moderator selector (e.g. a custom module's
    /// moderator-selection scenario), this is the [0.0, 1.0] confidence reported
    /// by that selector - the gap between the winner's score and the runner-up's score.
    /// Higher = the moderator was clearly the better subject-matter fit for this level.
    /// Lower = the moderator was a near-tie or defaulted because nothing cleared the
    /// threshold, so callers may want to surface a "low confidence" hint in the UI.
    /// </summary>
    [Key(8)] public float ModeratorSelectionConfidence { get; init; }

    /// <summary>
    /// True when the ascension gate rejected every candidate theme within
    /// DialecticsOptions.MaxAscensionGateRetries attempts (and any moderator-selection
    /// confidence fell below DialecticsOptions.SustainedConfidenceThreshold), so the
    /// dialectic remained on the current theme instead of forcing a low-confidence
    /// ascent. When true, NextTheme and ConnectiveSentence are empty and the current
    /// theme is carried forward.
    /// </summary>
    [Key(9)] public bool IsSustained { get; init; }

    /// <summary>
    /// Ascent-quality score for this synthesis level (0.0-1.0): rewards breaking
    /// new conceptual ground (reference coverage, novelty vs prior levels) and
    /// penalizes restating a prior position. Lower scores are a diagnostic signal for
    /// tuning the on-theme relevance threshold, not a hard gate.
    /// </summary>
    [Key(10)] public float AscentScore { get; init; }

    /// <summary>
    /// Negative-dialectic measure for this synthesis level (0.0-1.0): how far the synthesis
    /// holds the thesis/antithesis contradiction open rather than resolving it. High is a
    /// distinct success state, not a low-ascent failure. Independent of
    /// <see cref="AscentScore"/>; 0 when unavailable.
    /// </summary>
    [Key(11)] public float IrreducibleTension { get; init; }

    /// <summary>
    /// The moderator's own report of the sharpest live tension this synthesis does not
    /// close. Null when absent.
    /// </summary>
    [Key(12)] public string? RemainingDissensus { get; init; }

    /// <summary>
    /// The moderator's self-assessment (0.0-1.0) of how much this synthesis merely
    /// restates the question. Null when absent.
    /// </summary>
    [Key(13)] public float? RepeatsPremise { get; init; }

    /// <summary>
    /// Per-turn debate events from the non-streaming ascent path. Populated by
    /// <see cref="SeldonLattice.Infrastructure.Services.DialecticalAscentServiceBase"/> so the
    /// batch endpoint can export child spans to Sentinel just like the streaming path does.
    /// Null when the result came from a source that does not expose turn-level data.
    /// </summary>
    [Key(14)] public List<DebateTurnEvent>? DebateTurnEvents { get; init; }
}
