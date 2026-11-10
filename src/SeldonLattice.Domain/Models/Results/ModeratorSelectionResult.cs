using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.Domain.Models.Results;

/// <summary>
/// Result of moderator selection: which moderator was chosen, with what confidence, and why.
///</summary>
/// <remarks>
/// Lives in the Application layer (not Domain) because it carries an
/// <see cref="IModerator"/> reference, which is an Application-layer interface.
/// Domain-layer callers that only need the float confidence use
/// <c>DialecticalNode.ModeratorSelectionConfidence</c> or
/// <c>DialogueLevelResult.ModeratorSelectionConfidence</c> instead.
///</remarks>
public record ModeratorSelectionResult(
    /// <summary>The chosen moderator (always non-null - selection never returns a fallback</summary>
    IModerator Moderator,

    /// <summary>The canonical key for the chosen moderator (the role- or agent-identified moderator key).</summary>
    string ModeratorKey,

    /// <summary>
    /// Confidence in this selection (0.0-1.0). Reported as the *gap* between the winner's
    /// score and the runner-up's score, not the winner's raw score. Higher = clearer
    /// subject-matter fit; lower = near-tie or threshold default.
    ///</summary>
    float Confidence,

    /// <summary>
    /// The score the winning moderator achieved (0.0-1.0). When below
    /// the moderator-selection threshold this is a low-confidence
    /// pick (winner by default because nothing qualified).
    ///</summary>
    float WinnerScore,

    /// <summary>The losing moderator's score (0.0-1.0), for diagnostics and balancing</summary>
    float RunnerUpScore);
