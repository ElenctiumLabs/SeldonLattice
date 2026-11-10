using SeldonLattice.Domain.Models.Cycle;
using SeldonLattice.Domain.Options;

namespace SeldonLattice.Application.Services;

public partial class DialogueQualityGates
{
    /// <summary>
    /// Scans the current turn for any stalemate signal, returning the highest-priority one found.
    /// Signals are checked in priority order: CircularRestatement TalkingPast Entrenchment
    /// PremiseFork MissingInformation AgreementStagnation.
    /// Returns null if no stalemate is detected.
    /// </summary>
    /// <param name="state">Accumulated dialogue quality state with full history.</param>
    /// <param name="proposerResponse">Proposer's most recent response.</param>
    /// <param name="skepticResponse">Skeptic's most recent response.</param>
    /// <param name="dialectician1Name">Display name of the first dialectician (e.g. "proposer").</param>
    /// <param name="dialectician2Name">Display name of the second dialectician (e.g. "skeptic").</param>
    /// <param name="proposerToPreviousSimilarity">Bigram similarity of Dialectician 1's reply to the response two turns ago.</param>
    /// <param name="skepticToPreviousSimilarity">Bigram similarity of Dialectician 2's reply to the response two turns ago.</param>
    /// <param name="semanticDistance">Current cosine distance between embeddings (1 - cosine similarity).</param>
    /// <param name="recentDistances">Window of recent semantic distances for sustained-fork detection.</param>
    /// <param name="turn">Current turn index.</param>
    public StalemateSignal? DetectStalemate(
        DialogueQualityState state,
        string proposerResponse,
        string skepticResponse,
        string dialectician1Name,
        string dialectician2Name,
        double proposerToPreviousSimilarity,
        double skepticToPreviousSimilarity,
        double semanticDistance,
        IReadOnlyCollection<double> recentDistances,
        int turn)
    {
        // 1. Circular restatement: either agent is echoing content from 2 turns back
        if (proposerToPreviousSimilarity >= options.CircularRestatementThreshold ||
            skepticToPreviousSimilarity >= options.CircularRestatementThreshold)
            return StalemateSignal.CircularRestatement;

        // 2. Talking past each other: no disagreement markers in either response
        if (DetectTalkingPast(proposerResponse, skepticResponse, dialectician1Name, dialectician2Name))
            return StalemateSignal.TalkingPast;

        // 3. Entrenchment with no movement: high disagreement but no new arguments
        if (DetectEntrenchment(proposerResponse, skepticResponse, turn))
            return StalemateSignal.EntenchedNoMovement;

        // 4. Deep premise-level fork: semantic distance sustained above threshold for N turns
        if (DetectPremiseFork(recentDistances))
            return StalemateSignal.PremiseLevelFork;

        // 5. Missing information: epistemic hedges dominate both responses
        if (DetectMissingInformation(proposerResponse, skepticResponse))
            return StalemateSignal.MissingInformation;

        // 6. Agreement stagnation (lightest weight - only after 3+ turns of pure agreement)
        if (DetectAgreementStagnation(state, Math.Min(options.AgreementStagnationTurns, 3)))
            return StalemateSignal.AgreementStagnation;

        return null;
    }

    /// <summary>
    /// Returns true when both agents respond with zero cross-reference and zero disagreement markers -
    /// i.e., they are delivering parallel monologues rather than engaging each other.
    /// </summary>
    private static bool DetectTalkingPast(string proposerResponse, string skepticResponse,
        string dialectician1Name, string dialectician2Name)
    {
        var proposerLow = proposerResponse.ToLowerInvariant();
        var skepticLow = skepticResponse.ToLowerInvariant();

        // Cross-reference indicators: explicit mentions of the other's position
        string[] crossRefMarkers = ["you argue", "your position", "you claim", "you suggest",
                                     "your view", "you propose", "as you said", "your point",
                                     dialectician1Name.ToLowerInvariant(),
                                     dialectician2Name.ToLowerInvariant()];

        bool proposerCrossRef  = crossRefMarkers.Any(m => proposerLow.Contains(m, StringComparison.OrdinalIgnoreCase));
        bool skepticCrossRef = crossRefMarkers.Any(m => skepticLow.Contains(m, StringComparison.OrdinalIgnoreCase));
        bool proposerDisagrees  = DisagreementIndicators.Any(d => proposerLow.Contains(d, StringComparison.OrdinalIgnoreCase));
        bool skepticDisagrees = DisagreementIndicators.Any(d => skepticLow.Contains(d, StringComparison.OrdinalIgnoreCase));

        // Talking past = neither references the other AND neither disagrees
        return !proposerCrossRef && !skepticCrossRef && !proposerDisagrees && !skepticDisagrees;
    }

    /// <summary>
    /// Returns true when both agents actively disagree (hardened positions) but neither
    /// introduces new arguments - positions are static trenches, not evolving.
    /// Requires at least 2 turns before triggering to let the debate establish positions.
    /// </summary>
    private bool DetectEntrenchment(string proposerResponse, string skepticResponse, int turn)
    {
        // Don't trigger on turn 0 or 1 - let the debate establish positions first
        if (turn < 2) return false;

        var proposerLow = proposerResponse.ToLowerInvariant();
        var skepticLow = skepticResponse.ToLowerInvariant();

        bool proposerDisagrees  = DisagreementIndicators.Any(d => proposerLow.Contains(d, StringComparison.OrdinalIgnoreCase));
        bool skepticDisagrees = DisagreementIndicators.Any(d => skepticLow.Contains(d, StringComparison.OrdinalIgnoreCase));

        int proposerNewArgs  = CountSubstantiveContent(proposerResponse);
        int skepticNewArgs = CountSubstantiveContent(skepticResponse);

        return proposerDisagrees && skepticDisagrees && proposerNewArgs == 0 && skepticNewArgs == 0;
    }
    /// <summary>
    /// Returns true when semantic distance has been sustained above the premise-fork threshold
    /// for the required number of consecutive turns - signalling a deep, structural disagreement
    /// that surface-level debate cannot resolve.
    /// </summary>
    private bool DetectPremiseFork(IReadOnlyCollection<double> recentDistances)
    {
        if (recentDistances.Count < options.PremiseForkSustainedTurns)
            return false;

        // All recent distances must exceed the fork threshold
        return recentDistances
            .TakeLast(options.PremiseForkSustainedTurns)
            .All(d => d >= options.PremiseForkDistanceThreshold);
    }

    /// <summary>
    /// Returns true when both agents use multiple epistemic hedges in the same turn,
    /// indicating the debate has stalled due to a lack of grounding information.
    /// </summary>
    private static bool DetectMissingInformation(string proposerResponse, string skepticResponse)
    {
        string[] epistemicHedges =
        [
            "we cannot know", "it is unclear", "no one can say", "impossible to determine",
            "remains uncertain", "we have no way", "without evidence", "we can only speculate",
            "it depends", "there is no answer"
        ];

        int proposerHedges = epistemicHedges.Count(h =>
            proposerResponse.Contains(h, StringComparison.OrdinalIgnoreCase));
        int skepticHedges = epistemicHedges.Count(h =>
            skepticResponse.Contains(h, StringComparison.OrdinalIgnoreCase));

        // Both agents must hedge at least twice each
        return proposerHedges >= 2 && skepticHedges >= 2;
    }

    private static bool ContainsAgreementMarkers(string text)
    {
        var lower = text.ToLowerInvariant();
        return AgreementMarkers.Any(m => lower.Contains(m, StringComparison.OrdinalIgnoreCase));
    }
}
