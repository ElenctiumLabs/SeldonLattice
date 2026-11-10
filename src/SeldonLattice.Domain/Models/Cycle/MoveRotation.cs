namespace SeldonLattice.Domain.Models.Cycle;

/// <summary>
/// Maps rhetorical moves to their prompt instructions. Move selection is handled
/// separately and must not impose a fixed turn-by-turn sequence.
/// </summary>
public static class MoveRotation
{
    /// <summary>
    /// Gets a human-readable suggestion for the specified move.
    /// </summary>
    public static string GetInstruction(RhetoricalMove move) => move switch
    {
        RhetoricalMove.EmpiricalCase => "Cite one specific, checkable historical or empirical fact (name/date/measurement) that bears on the theme.",
        RhetoricalMove.FormalDistinction => "If a key term is being used in different senses, make the distinction precise and show whether it changes the disagreement.",
        RhetoricalMove.Reductio => "Take your opponent's last claim to its logical extreme and show what breaks.",
        RhetoricalMove.CrossDomainAnalogy => "Use an analogy from a domain neither of you has invoked yet this dialogue.",
        _ => "Introduce a genuine counterexample to the current working definition.",
    };
}
