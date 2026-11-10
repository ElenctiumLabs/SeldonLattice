namespace SeldonLattice.Domain.Options;

/// <summary>
/// Configuration options for dialogue quality gates.
/// Controls thresholds for ceremony detection, similarity checks, and content validation.
/// Maps to the DIALOGUE_QUALITY_GATES_CONFIG.yaml specification.
/// </summary>
public class QualityGateOptions
{
    /// <summary>
    /// Maximum number of turns before forced termination.
    /// </summary>
    public int MaxTurns { get; init; } = 10;

    /// <summary>
    /// Consecutive farewell-containing responses that trigger forced exit.
    /// </summary>
    public int ConsecutiveFarewellsThreshold { get; init; } = 8;

    /// <summary>
    /// Similarity threshold (0.0-1.0) above which content is considered repetitive.
    /// </summary>
    public double SimilarityThreshold { get; init; } = 0.80;

    /// <summary>
    /// Number of consecutive agreement turns without new ideas to trigger stagnation exit.
    /// </summary>
    public int AgreementStagnationTurns { get; init; } = 4;

    /// <summary>
    /// Ceremony score above which a soft warning is logged.
    /// </summary>
    public double CeremonyScoreWarning { get; init; } = 12.0;

    /// <summary>
    /// Word count above which a response is considered verbose/padded.
    /// </summary>
    public int VerboseWordCount { get; init; } = 1000;

    // --- Length bracket (0-10 points of the quality score) ---
    //
    // These MUST be aligned with the configured dialectician reply length, or the length
    // component is unreachable and the score silently caps below 100.
    //
    // The defaults here were written for long replies (400+ words). A module configured with
    // DialecticsOptions.DialecticianTargetWordCount = 80 produces ~88-word replies, which earn
    // ZERO length points - the observed "stuck at 70/80%" in early runs. The
    // measurement layer and the prompt layer have to agree; whichever one moves, move both.
    //
    // Verify alignment by comparing OptimalLengthMin/Max against the configured
    // DialecticianTargetWordCount..DialecticianMaxWordCount band.

    /// <summary>
    /// Lower bound of the optimal-length bracket (full 10 points). Default 400.
    /// Should be at or below the configured DialecticianTargetWordCount.
    /// </summary>
    public int OptimalLengthMin { get; init; } = 400;

    /// <summary>
    /// Upper bound of the optimal-length bracket (full 10 points). Default 800.
    /// Should be at or above the configured DialecticianTargetWordCount.
    /// </summary>
    public int OptimalLengthMax { get; init; } = 800;

    /// <summary>Upper bound of the acceptable-verbose bracket (7 points). Default 1000.</summary>
    public int AcceptableLengthMax { get; init; } = 1000;

    /// <summary>Upper bound of the notably-padded bracket (4 points). Default 1400.</summary>
    public int PaddedLengthMax { get; init; } = 1400;

    /// <summary>Upper bound of the bloated bracket (2 points). Default 2000.</summary>
    public int BloatedLengthMax { get; init; } = 2000;

    /// <summary>
    /// Similarity threshold for soft warning (0.0-1.0).
    /// </summary>
    public double ContentRepetitionWarning { get; init; } = 0.75;

    /// <summary>
    /// Quality score below which dialogue is force-exited (0-100).
    /// </summary>
    public double QualityScoreExitThreshold { get; init; } = 35.0;

    /// <summary>
    /// Number of consecutive low-quality turns before forced exit.
    /// </summary>
    public int LowQualityConsecutiveTurns { get; init; } = 3;

    /// <summary>
    /// Minimum turn count before the similarity gate fires.
    /// </summary>
    public int MinTurnsForSimilarityCheck { get; init; } = 3;

    /// <summary>
    /// Whether to enable quality gate checking.
    /// </summary>
    public bool Enabled { get; init; } = true;

    // --- Stalemate detection thresholds ---

    /// <summary>
    /// Bigram similarity to the response from two turns ago above which circular restatement
    /// is detected. Default 0.50 (50% overlap signals a loop).
    /// </summary>
    public double CircularRestatementThreshold { get; init; } = 0.50;

    /// <summary>
    /// Cosine distance threshold above which a sustained divergence is considered a deep
    /// premise-level fork. Default 0.75 (high semantic distance).
    /// </summary>
    public double PremiseForkDistanceThreshold { get; init; } = 0.75;

    /// <summary>
    /// Number of consecutive turns the semantic distance must exceed PremiseForkDistanceThreshold
    /// before a premise fork is declared. Default 4 turns.
    /// </summary>
    public int PremiseForkSustainedTurns { get; init; } = 4;

    // --- On-theme relevance detection thresholds ---

    /// <summary>
    /// Whether the on-theme relevance gate is active. When enabled, responses are embedded and
    /// compared against the root question's embedding to catch topic drift.
    /// </summary>
    public bool RelevanceGateEnabled { get; init; } = true;

    /// <summary>
    /// Cosine similarity (0.0-1.0) between a response embedding and the root-theme embedding
    /// below which the response is considered off-topic. Default 0.28.
    /// </summary>
    public double OnThemeRelevanceThreshold { get; set; } = 0.28;

    /// <summary>
    /// Number of consecutive off-topic turns before the debate is force-exited.
    /// </summary>
    public int RelevanceConsecutiveTurns { get; init; } = 2;

    /// <summary>
    /// Minimum turn count before the relevance gate fires. Gives agents a grace period
    /// to establish their opening stances.
    /// </summary>
    public int MinTurnsForRelevanceCheck { get; init; } = 1;
}