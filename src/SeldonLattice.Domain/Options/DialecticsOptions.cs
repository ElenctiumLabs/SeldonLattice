namespace SeldonLattice.Domain.Options;

/// <summary>
/// Scoring mode for the Ascent-quality objective. See
/// <see cref="DialecticsOptions.AscentScorerMode"/>.
/// </summary>
public enum AscentScorerMode
{
    /// <summary>Legacy 0.5·betweenness + 0.5·advance blend (non-discriminating, inverted on pole restatement).</summary>
    Legacy = 0,

    /// <summary>Spec-conformant: advance as objective, betweenness ≥ floor as hard constraint (AUC 1.000 vs pole restatement).</summary>
    Spec = 1
}

/// <summary>
/// Configuration options for the dialectical ascent process.
/// Controls the dialogue dynamics between philosophical agents and quantum measurement integration.
/// </summary>
public class DialecticsOptions
{
    /// <summary>
    /// The user's name for agents to address them by.
    /// </summary>
    public string UserName { get; set; } = "";

    /// <summary>
    /// The threshold for statistical dialogue convergence.
    /// When the standard deviation of cosine distances over the last StabilityWindow turns
    /// falls below this value, the dialogue is considered stable enough for synthesis.
    /// Raise if equilibrium is never reached; lower it for stricter convergence.
    /// </summary>
    public float StabilityThreshold { get; init; } = 0.08f;

    /// <summary>
    /// The number of recent turns to consider when computing stability metrics.
    /// A rolling window of this size is maintained for distance and variability calculations.
    /// Smaller windows react faster but are noisier; larger windows are smoother but slower.
    /// Default is 3 turns.
    /// </summary>
    public int StabilityWindow { get; init; } = 3;

    /// <summary>
    /// Maximum number of turns before forcing synthesis, even if equilibrium isn't reached.
    /// Prevents infinite dialogue loops when convergence doesn't occur naturally.
    /// Each turn consists of one dialectician response and one counter-response.
    /// Default is 10 turns.
    /// </summary>
    public int MaxTurns { get; init; } = 10;

    /// <summary>
    /// Maximum depth of the dialectical ascent pyramid.
    /// Each level ascends to a more fundamental question derived from the previous synthesis.
    /// The process can be terminated early if a satisfactory conclusion is reached.
    /// Default is 5 levels.
    /// </summary>
    public int MaxAscentLevels { get; init; } = 5;

    /// <summary>
    /// The default intensity level used by dialectician agents when none is supplied.
    /// </summary>
    public int DefaultIntensity { get; init; } = 5;

    /// <summary>
    /// Maximum number of times the Mediator may interject per ascent level to break a stalemate.
    /// Once this budget is exhausted the existing hard-stop quality gates take over.
    /// Default is 3 interjections per level.
    /// </summary>
    public int MaxInterjections { get; init; } = 3;

    /// <summary>
    /// When true, a tiered dialectical pressure directive is appended to each user message
    /// delivered to the debating agents, compelling them to advance the argument rather than
    /// restate or agree. The directive escalates from mild medium hard as turns increase
    /// and quality scores drop. The directive is embedded in the LLM history only - it does
    /// NOT appear in DebateTurnEvent.Content, so the UI transcript stays clean.
    /// Default is true.
    /// </summary>
    public bool AscentPressureEnabled { get; init; } = true;

    /// <summary>
    /// When enabled, a context-sensitive rhetorical move may be suggested after a response
    /// shows repetition and low novelty. The suggestion is optional and is never selected
    /// from a fixed phase or turn sequence. Default is true.
    /// </summary>
    public bool EnableMoveScheduler { get; init; } = true;

    /// <summary>
    /// Scoring mode for the ascent-quality objective (<c>AscentQualityScorer</c>).
    /// <para><b>Legacy</b> (default): <c>clamp01(0.5·betweenness + 0.5·advance)</c>. Measured
    /// to be non-discriminating between good and
    /// degraded syntheses and INVERTED on pole restatement (AUC 0.234): the betweenness term
    /// saturates for a synthesis that simply restates a pole, so pole restatements score
    /// HIGHER than real syntheses. Kept only for continuity with existing telemetry.</para>
    /// <para><b>Spec</b>: the mode prescribed -
    /// advance is the continuous objective, betweenness is a hard constraint (score is zeroed
    /// when <c>betweenness &lt; AscentBetweennessFloor</c>). Separates real syntheses from
    /// pole restatements perfectly in AUC 1.000, d +3.77.</para>
    /// Default is Legacy until cross-module validation completes (Limitation: n=8,
    /// one session); flip to Spec after re-deriving <c>LowAscentThreshold</c>.
    /// </summary>
    public AscentScorerMode AscentScorerMode { get; init; } = AscentScorerMode.Legacy;

    /// <summary>
    /// Betweenness floor used as the hard constraint in <see cref="AscentScorerMode.Spec"/>.
    /// A synthesis whose betweenness (cosine to the pole midpoint) falls below this floor is
    /// not grounded in the poles at all and scores 0 regardless of advance. Default 0.60,
    /// the value validated.
    /// </summary>
    public float AscentBetweennessFloor { get; init; } = 0.60f;
    
    /// <summary>
    /// Minimum average NewArgumentCount (summed across both agents per turn, averaged over
    /// StabilityWindow turns) required to treat a stable-embedding-distance state as productive
    /// "renewal" rather than stagnant "saturation"/"crisis". Prevents genuinely novel, high-content
    /// debates from being trapped purely because both agents share vocabulary within one theme.
    /// Default is 1.0.
    /// </summary>
    public float NoveltyThreshold { get; init; } = 1.0f;

    /// <summary>
    /// Maximum number of times the ascension gate (MediatorAgent.EvaluateAscensionAsync)
    /// may reject a candidate NextTheme and ask for a redo before the ascent gives up. When the
    /// gate exhausts its retries the level produces a "Sustained" outcome - the current theme is
    /// carried forward with no forced next theme (see DialogueLevelResult.IsSustained) - unless
    /// a moderator selector reported confidence at or above SustainedConfidenceThreshold, in which
    /// case the last candidate is accepted anyway and flagged via AscensionValidated = false so
    /// downstream consumers can surface it for review rather than silently drifting.
    /// Total candidates evaluated per level is at most this value + 1.
    /// Default is 2 (i.e. up to 3 attempts total per level).
    /// </summary>
    public int MaxAscensionGateRetries { get; init; } = 2;

    /// <summary>
    /// Maximum number of times the synthesis metaphor generation may be retried when
    /// the result is too similar to a prior synthesis (resonance above threshold) or
    /// reuses a forbidden image domain. Each retry feeds the forbidden-domains list
    /// back in so the LLM is forced to pick a genuinely different image domain.
    /// Default is 2 (i.e. up to 3 attempts total per level).
    /// </summary>
    public int MaxSynthesisRetries { get; init; } = 2;

    /// <summary>
    /// Cosine-similarity threshold for rejecting a synthesis metaphor as too similar
    /// to a prior one. When ComputeResonance() returns a value at or above this
    /// threshold, the metaphor is considered a near-repeat and retried with a broader
    /// forbidden-domains list. Tune against real ResonanceScore values from your
    /// mediator_synthesis nodes - set near whatever value separates "different metaphor,
    /// same idea" from "genuinely new" in your actual run history.
    /// Default is 0.55f.
    /// </summary>
    public float SynthesisResonanceThreshold { get; init; } = 0.55f;

    /// <summary>
    /// Minimum variability (absolute change in cosine similarity between turns)
    /// required to consider the dialogue as genuinely evolving rather than stagnant.
    /// Below this threshold the stability check treats the dialogue as converged.
    /// Default is 0.01f.
    /// </summary>
    public float VariabilityMinimum { get; init; } = 0.01f;

    /// <summary>
    /// Maximum number of recent synthesis embeddings retained for resonance comparison.
    /// Controls how far back the system looks when checking if a new synthesis metaphor
    /// is too similar to a prior one.
    /// Default is 10.
    /// </summary>
    public int SynthesisMemoryWindow { get; init; } = 10;

    /// <summary>
    /// Maximum number of recent turn pairs to retain in each dialectician's chat history.
    /// Older turns are trimmed to reduce LLM context size and improve inference speed.
    /// The system prompt (index 0) and ascent context (index 1, if present) are always preserved.
    /// Each turn pair consists of 2 messages: a user message (opponent's text) and an assistant message (own response).
    /// Mediation messages within the retained window are also preserved.
    /// Default is 3 (retaining the last 3 turn pairs = 6 messages plus potential mediation).
    /// </summary>
    public int MaxContextTurns { get; init; } = 3;

    /// <summary>
    /// Maximum number of previous ascent levels to include in the ascent context.
    /// When > 0, only the most recent N levels' syntheses are carried forward.
    /// When 0 or negative, all previous levels are included (legacy behavior).
    /// Default is 1 (fixed-context: only the immediate previous round's synthesis).
    /// </summary>
    public int MaxAscentContextLevels { get; init; } = 1;

    /// <summary>
    /// Intensity level used when enhancing prompts during saturation/crisis phase boost.
    /// Default is 10.
    /// </summary>
    public int SaturationBoostIntensity { get; init; } = 10;

    /// <summary>
    /// Number of top citations to fetch from the citation provider for each synthesis node.
    /// Default is 5.
    /// </summary>
    public int CitationTopN { get; init; } = 5;

    /// <summary>
    /// Threshold for variability below which a stable dialogue is classified as "crisis" phase
    /// rather than "renewal" in CyclePhaseDetector.
    /// Default is 0.1.
    /// </summary>
    public float VariabilityCrisisThreshold { get; init; } = 0.1f;

    /// <summary>
    /// Minimum turn count before a stable dialogue can be classified as "crisis" phase.
    /// Prevents premature crisis classification in early turns.
    /// Default is 6.
    /// </summary>
    public int CrisisMinTurns { get; init; } = 8;

    /// <summary>
    /// Maximum turn count for "growth" phase classification. After this many turns,
    /// a non-stable dialogue transitions from growth to saturation.
    /// Default is 4.
    /// </summary>
    public int GrowthMaxTurns { get; init; } = 8;

    /// <summary>
    /// Threshold for velocity (rate of change between consecutive turns' cosine distance) below which
    /// the dialogue tension is considered to have plateaued (velocity vector calculus, Primitive 2.1).
    /// Default is 0.05.
    /// </summary>
    public float VelocityPlateauThreshold { get; init; } = 0.05f;

    /// <summary>
    /// Lower absolute distance threshold when velocity has plateaued to classify the state
    /// as "saturation" (lexical looping / circular reasoning).
    /// Default is 0.20.
    /// </summary>
    public float VelocitySaturationThreshold { get; init; } = 0.20f;

    /// <summary>
    /// Upper absolute distance threshold when velocity has plateaued to classify the state
    /// as "crisis" (structural deadlock / talking past each other).
    /// Default is 0.60.
    /// </summary>
    public float VelocityCrisisThreshold { get; init; } = 0.60f;

    /// <summary>
    /// Threshold above which a response's similarity to its own previous response
    /// triggers a pressure directive ("your last response was very similar").
    /// Default is 0.85.
    /// </summary>
    public float SimilarityToPreviousHigh { get; init; } = 0.85f;

    /// <summary>
    /// Threshold above which a response's ceremony score triggers a pressure directive
    /// ("focus on substance rather than rhetorical flourishes").
    /// Default is 2.0.
    /// </summary>
    public float CeremonyScoreHigh { get; init; } = 2.0f;

    /// <summary>
    /// Intensity level at or above which the "MAXIMUM intensity" enhanced prompt is used
    /// in dialectician CreateHistory methods.
    /// Default is 10.
    /// </summary>
    public int EnhancedPromptIntensityThreshold { get; init; } = 10;

    /// <summary>
    /// Maximum character length for dialectician response snippets used in
    /// MediatorAgent interjection prompts.
    /// Default is 800.
    /// </summary>
    public int InterjectionSnippetMaxLength { get; init; } = 800;

    /// <summary>
    /// Default TensionIntensity value returned by mediators when the LLM response
    /// cannot be parsed.
    /// Default is 0.5.
    /// </summary>
    public float DefaultTensionIntensity { get; init; } = 0.5f;

    /// <summary>
    /// Threshold below which a dynamic moderator-selector confidence counts as "low
    /// confidence" when deciding whether a level that exhausted its ascension
    /// gate retries should produce a "Sustained" outcome (declining to manufacture a
    /// next theme) instead of forcing the last rejected candidate through.
    /// Modules without a moderator selector (e.g. the philosophy module) always trigger Sustained
    /// on gate exhaustion, since there is no confidence signal to consider.
    /// Default is 0.5.
    /// </summary>
    public float SustainedConfidenceThreshold { get; init; } = 0.5f;

    /// <summary>
    /// Maximum character length for mediator messages before truncation in
    /// ChatHistoryExtensions.FormatForMediator.
    /// Default is 3000.
    /// </summary>
    public int MediatorMessageTruncateLength { get; init; } = 5000;

    /// <summary>
    /// Target word count for dialectician responses.
    /// Default: 80
    /// </summary>
    public int DialecticianTargetWordCount { get; init; } = 80;

    /// <summary>
    /// Maximum word count for dialectician responses.
    /// Default: 180
    /// </summary>
    public int DialecticianMaxWordCount { get; init; } = 180;

    /// <summary>
    /// Target word count for moderator synthesis responses.
    /// Default: 80
    /// </summary>
    public int ModeratorTargetWordCount { get; init; } = 80;

    /// <summary>
    /// Maximum word count for moderator synthesis responses.
    /// Default: 180
    /// </summary>
    public int ModeratorMaxWordCount { get; init; } = 180;

    /// <summary>
    /// Target word count for mediator responses.
    /// Default: 80
    /// </summary>
    public int MediatorTargetWordCount { get; init; } = 80;

    /// <summary>
    /// Maximum word count for mediator responses.
    /// Default: 180
    /// </summary>
    public int MediatorMaxWordCount { get; init; } = 180;

    /// <summary>
    /// Maximum word count for debate rules directive (the "under N words" cap).
    /// Default: 80
    /// </summary>
    public int DialecticianDebateMaxWords { get; init; } = 80;

    /// <summary>
    /// Maximum character length for moderator theme sanitization.
    /// Default: 180
    /// </summary>
    public int ModeratorThemeMaxLength { get; init; } = 180;

    /// <summary>
    /// Maximum character length for moderator connective sentence sanitization.
    /// Default: 300
    /// </summary>
    public int ModeratorConnectiveMaxLength { get; init; } = 300;

    /// <summary>
    /// When true, prior sessions' <see cref="SeldonLattice.Domain.Models.Storage.ArgumentClaimNode">claims</see>
    /// overlapping the session's root question are recalled (ClaimMemoryRetriever) and folded
    /// into each level's opening user turn as "prior adjudicated positions" grounding - the
    /// cross-session memory. Retrieved claims are treated
    /// as provisional and must be engaged, not echoed. Default is false (off until observed
    /// to improve validity on repeat root questions).
    /// </summary>
    public bool EnableCrossSessionMemory { get; init; }

    /// <summary>
    /// Maximum number of prior-session claims recalled per session when cross-session memory
    /// is enabled. Default is 5.
    /// </summary>
    public int CrossSessionMemoryMaxClaims { get; init; } = 5;

    /// <summary>
    /// Minimum token-overlap similarity for a prior claim to be recalled when cross-session
    /// memory is enabled. Default is 0.20.
    /// </summary>
    public float CrossSessionMemoryMinSimilarity { get; init; } = 0.20f;

    /// <summary>
    /// Scoring mode for cross-session claim retrieval. TokenOverlap (default) uses
    /// deterministic Jaccard similarity; Embedding uses the provider's embedding service
    /// for cosine similarity. Embedding mode requires a registered embedding service
    /// and falls back to TokenOverlap if unavailable.
    /// </summary>
    public ClaimMemoryMode CrossSessionMemoryMode { get; init; } = ClaimMemoryMode.TokenOverlap;
}
