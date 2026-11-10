namespace SeldonLattice.Domain.Models.Cycle;

/// <summary>
/// A lightweight per-agent affective state computed deterministically each turn
/// from already-available quality metrics and dialogue trajectory data.
/// 
/// This is NOT a mood system. It represents the <em>epistemic affect</em> -
/// the emotional register appropriate to a thinker of a specific role when
/// confronted with a particular kind of rhetorical pressure at a given point
/// in the dialectic. All fields are normalised to [0, 1] or [-1, +1] and
/// derived without any additional LLM call.
/// 
/// Injected into the private [REASONING] guidance of the ascent directive so
/// each agent can calibrate its tone; never surfaced in the public [RESPONSE].
/// </summary>
public record EmotionalVolatilityState
{
    /// <summary>
    /// How energised/activated the agent is this turn (0 = inert; 1 = fully charged).
    /// Rises with high aporia and sharp momentum; falls under saturation or stalemate.
    /// </summary>
    public double Arousal { get; init; }

    /// <summary>
    /// Affective tone: negative = hostile/frustrated; positive = galvanised/emboldened.
    /// Driven by the agent's own argumentative success (new arguments landed) vs.
    /// the directness of the opponent's opposition.
    /// </summary>
    public double Valence { get; init; }

    /// <summary>
    /// Trajectory of the dialogue: positive = accelerating/deepening; negative = decelerating/stalling.
    /// Computed as the normalised delta of quality score turn-over-turn.
    /// </summary>
    public double Momentum { get; init; }

    /// <summary>
    /// How directly this turn's opponent response attacked the agent's core claim (0 = glancing; 1 = direct hit).
    /// Derived from the opponent's ceremony score (low ceremony = direct attack) and new argument count.
    /// </summary>
    public double Provocation { get; init; }

    /// <summary>
    /// Neutral baseline state - used when insufficient data is available (e.g. turn 0).
    /// </summary>
    public static readonly EmotionalVolatilityState Neutral = new()
    {
        Arousal = 0.4,
        Valence = 0.0,
        Momentum = 0.0,
        Provocation = 0.0,
    };

    /// <summary>
    /// Returns true when the EVL is significant enough to warrant injecting an affect phrase
    /// into the ascent directive. Guards against noise on calm early turns.
    /// Fires when Arousal exceeds 0.55 or the absolute Momentum change exceeds 0.15.
    /// </summary>
    public bool IsSignificant(double previousMomentum = 0.0)
        => Arousal > 0.55 || Math.Abs(Momentum - previousMomentum) > 0.15;
}
