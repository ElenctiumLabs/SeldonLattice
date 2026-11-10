namespace SeldonLattice.Domain.Models.Cycle;

/// <summary>
/// Identifies the type of stalemate pattern that triggered a mediator interjection.
/// Each value maps to a distinct intervention strategy.
/// </summary>
public enum StalemateSignal
{
    /// <summary>Both agents restate the same bigrams as two turns ago - circular loop.</summary>
    CircularRestatement,

    /// <summary>Agents respond without engaging each other's arguments - parallel monologues.</summary>
    TalkingPast,

    /// <summary>High disagreement but no new arguments; positions are hardened and static.</summary>
    EntenchedNoMovement,

    /// <summary>Semantic distance is very high and sustained - root premise conflict.</summary>
    PremiseLevelFork,

    /// <summary>Both agents repeat epistemic hedges - debate blocked by missing facts.</summary>
    MissingInformation,

    /// <summary>Both agents agree for multiple turns with no new ideas introduced.</summary>
    AgreementStagnation,
}