using SeldonLattice.Domain.Models.Cycle;

namespace SeldonLattice.Domain.Utilities;

/// <summary>
/// Computes an <see cref="EmotionalVolatilityState"/> for a single agent turn
/// purely from already-available quality metrics and dialogue trajectory data.
/// No additional LLM call is needed.
///
/// Aporia (the degree of productive dialectical tension) is proxied from the
/// opponent's metrics rather than requiring a LLM evaluation, making
/// the computation module-agnostic (works for every module).
/// </summary>
public static class EmotionalVolatilityComputer
{
    /// <summary>
    /// Computes the EVL for the agent whose <paramref name="ownMetrics"/> is provided,
    /// given the <paramref name="opponentMetrics"/> from the opponent's last turn.
    /// </summary>
    /// <param name="ownMetrics">Quality metrics from this agent's previous turn (null on turn 0 or level opening).</param>
    /// <param name="opponentMetrics">Quality metrics from the opponent's most recent turn (null when opponent has not yet spoken).</param>
    /// <param name="previousQualityScore">This agent's quality score from the turn before <paramref name="ownMetrics"/>, used to compute momentum delta.</param>
    /// <param name="phase">Current cycle phase name (emergence | growth | renewal | saturation | crisis).</param>
    /// <param name="turn">Zero-based turn index within the current level.</param>
    public static EmotionalVolatilityState Compute(
        ResponseQualityMetrics? ownMetrics,
        ResponseQualityMetrics? opponentMetrics,
        double previousQualityScore,
        string phase,
        int turn)
    {
        // Not enough data to say anything meaningful yet.
        if (ownMetrics is null && opponentMetrics is null)
            return EmotionalVolatilityState.Neutral;

        //  Proxy aporia 
        // High aporia = the opponent's argument was genuinely different from
        // the agent's own last turn (low similarity to previous = true opposition)
        // combined with the opponent having produced new arguments.
        var proxyAporia = ProxyAporia(ownMetrics, opponentMetrics);

        //  Saturation pressure 
        var saturationPressure = phase switch
        {
            "saturation" => 0.35,
            "crisis"     => 0.55,
            _            => 0.0,
        };

        //  Arousal 
        // Driven by aporia and own argumentative momentum; dampened by saturation.
        var arousal = Clamp(
            0.3
            + (proxyAporia * 0.5)
            - (saturationPressure * 0.25)
            + (ownMetrics is { NewArgumentCount: > 1 } ? 0.1 : 0.0),
            0.0, 1.0);

        //  Valence 
        // Positive: agent is landing clean arguments.
        // Negative: agent is being directly, effectively challenged.
        var ownSuccess = ownMetrics is not null
            ? Math.Min(ownMetrics.NewArgumentCount * 0.15, 0.45)
            : 0.0;
        var opponentPressure = opponentMetrics is not null
            ? (1.0 - opponentMetrics.CeremonyScore) * Math.Min(opponentMetrics.NewArgumentCount * 0.2, 0.6)
            : 0.0;

        var valence = Clamp(ownSuccess - opponentPressure, -1.0, 1.0);

        //  Momentum 
        // Positive = quality improving vs. own previous turn; negative = degrading.
        var currentQuality = ownMetrics?.QualityScore ?? 50.0;
        var momentum = Clamp((currentQuality - previousQualityScore) / 50.0, -1.0, 1.0);

        //  Provocation 
        // How directly the opponent's turn attacked rather than danced around.
        var provocation = opponentMetrics is not null
            ? Clamp(
                (1.0 - opponentMetrics.CeremonyScore) * (opponentMetrics.NewArgumentCount * 0.25),
                0.0, 1.0)
            : 0.0;

        return new EmotionalVolatilityState
        {
            Arousal     = arousal,
            Valence     = valence,
            Momentum    = momentum,
            Provocation = provocation,
        };
    }

    //  Internal helpers 

    private static double ProxyAporia(
        ResponseQualityMetrics? own,
        ResponseQualityMetrics? opponent)
    {
        if (own is null && opponent is null) return 0.5;

        // High similarity between own and opponent low aporia (they're converging).
        var divergence = own is not null ? 1.0 - own.SimilarityToPrevious : 0.5;

        // Opponent producing fresh arguments drives genuine tension.
        var opponentNovelty = opponent is not null
            ? Math.Min(opponent.NewArgumentCount * 0.2, 0.5)
            : 0.25;

        // Opponent not using ceremonial concessions = real engagement.
        var opponentDirectness = opponent is not null ? 1.0 - opponent.CeremonyScore : 0.5;

        return Clamp(
            (divergence * 0.4) + (opponentNovelty * 0.35) + (opponentDirectness * 0.25),
            0.0, 1.0);
    }

    private static double Clamp(double value, double min, double max)
        => Math.Max(min, Math.Min(max, value));
}
