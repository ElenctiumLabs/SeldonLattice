using SeldonLattice.Domain.Models.Cycle;
using SeldonLattice.Domain.Models.Results;

namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Extended moderator contract for agents that drive the full dialectical ascent
/// cycle: synthesizing a metaphor from the debate, proposing the next inquiry
/// theme, evaluating whether that theme genuinely generalises the root question,
/// and generating targeted interjections to break stalemates.
///
/// <see cref="IModerator"/> covers the basic synthesis path used by most moderators.
/// <see cref="IDialecticalModerator"/> adds the four methods required by the
/// full ascent path so that <see cref="DialecticalAscentServiceBase"/> never
/// needs to cast to a concrete agent type.
/// </summary>
public interface IDialecticalModerator : IModerator
{
    /// <summary>
    /// Generates a synthesis metaphor from the two dialecticians' final texts,
    /// selecting an image domain not already used this session.
    /// </summary>
    Task<MetaphorResult> SynthesizeMetaphorAsync(
        string theme,
        string dialectician1Reply,
        string dialectician2Reply,
        string phase,
        IReadOnlyList<string> forbiddenDomains,
        CancellationToken ct = default,
        int maxRetries = 2);

    /// <summary>
    /// Proposes the next dialectical inquiry theme, ascending from the current
    /// synthesis toward a more general formulation of the root question.
    /// </summary>
    /// <param name="metaphor">The synthesis metaphor produced at the current level.</param>
    /// <param name="currentTheme">The current level's question.</param>
    /// <param name="rootTheme">The authored root question the whole inquiry serves.</param>
    /// <param name="priorRejectionFeedback">
    /// Reasoning from a gate rejection of an earlier candidate at this level, when there was one.
    /// </param>
    /// <param name="climbedThemes">
    /// The rungs already climbed in this inquiry, in order, excluding the current question.
    /// Ladder memory: without it the moderator restates earlier rungs in new words.
    /// </param>
    /// <param name="synthesisThesis">
    /// The plain claim the current level's debate produced, alongside its metaphor. The primary
    /// path previously passed only the metaphor, so the moderator never saw the thesis.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<NextThemeResult> SynthesizeNextThemeAsync(
        string metaphor,
        string currentTheme,
        string rootTheme,
        string? priorRejectionFeedback = null,
        IReadOnlyList<string>? climbedThemes = null,
        string? synthesisThesis = null,
        CancellationToken ct = default);

    /// <summary>
    /// Evaluates whether a candidate next-theme genuinely generalises from the
    /// current level's inquiry rather than drifting to an unrelated topic, and
    /// whether it opens a rung the inquiry has not already climbed.
    /// </summary>
    /// <param name="rootTheme">The authored root question the whole inquiry serves.</param>
    /// <param name="candidateTheme">The candidate next rung.</param>
    /// <param name="connectiveSentence">The candidate's own claimed connection to the root.</param>
    /// <param name="climbedThemes">
    /// The rungs already climbed in this inquiry, in order, excluding the current question.
    /// Without it the gate cannot tell a new rung from a restatement of an earlier one.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<AscensionVerdict> EvaluateAscensionAsync(
        string rootTheme,
        string candidateTheme,
        string connectiveSentence,
        IReadOnlyList<string>? climbedThemes = null,
        CancellationToken ct = default);

    /// <summary>
    /// Answers for the moderator's claim that the current rung is spent and no warranted next
    /// question exists. The gate judges the claim itself - whether the inquiry has genuinely
    /// reached its limit at this point - rather than judging a candidate, because there is none.
    /// </summary>
    /// <param name="rootTheme">The authored root question the whole inquiry serves.</param>
    /// <param name="exhaustionReason">The moderator's stated reason for declining to propose.</param>
    /// <param name="climbedThemes">
    /// The rungs already climbed in this inquiry, in order, excluding the current question. The
    /// gate needs them to tell an inquiry that is genuinely spent from one the moderator simply
    /// cannot be bothered to continue.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<AscensionVerdict> EvaluateExhaustionAsync(
        string rootTheme,
        string exhaustionReason,
        IReadOnlyList<string>? climbedThemes = null,
        CancellationToken ct = default);

    /// <summary>
    /// Generates a targeted interjection in the moderator's own role to break
    /// a detected stalemate. The interjection is injected into both dialecticians'
    /// histories so the next turn is forced off the stuck track.
    /// </summary>
    /// <param name="signal">The stalemate type that was detected.</param>
    /// <param name="theme">The current debate theme.</param>
    /// <param name="dialectician1Text">Most recent text from dialectician 1.</param>
    /// <param name="dialectician2Text">Most recent text from dialectician 2.</param>
    /// <param name="d1Name">Display name of dialectician 1 (used in the prompt).</param>
    /// <param name="d2Name">Display name of dialectician 2 (used in the prompt).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string> GenerateInterjectionAsync(
        StalemateSignal signal,
        string theme,
        string dialectician1Text,
        string dialectician2Text,
        string d1Name,
        string d2Name,
        CancellationToken ct = default);
}
