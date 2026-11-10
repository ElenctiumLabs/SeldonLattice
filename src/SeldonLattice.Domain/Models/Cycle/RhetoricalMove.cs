namespace SeldonLattice.Domain.Models.Cycle;

/// <summary>
/// Defines different rhetorical moves that agents can use to vary their argument structure
/// turn to turn, preventing repetitive "counterexample refine" patterns.
/// </summary>
public enum RhetoricalMove
{
    /// <summary>Default move - propose a counterexample to the current working definition.</summary>
    ConceptualCounterexample,
    
    /// <summary>Cite a concrete historical or scientific fact (name/date/measurement) that bears on the theme.</summary>
    EmpiricalCase,
    
    /// <summary>Draw a precise distinction between two senses of a key term the other philosopher is conflating.</summary>
    FormalDistinction,
    
    /// <summary>Take the opponent's last claim to its logical extreme and show what breaks.</summary>
    Reductio,
    
    /// <summary>Use an analogy from a domain neither agent has invoked yet this dialogue.</summary>
    CrossDomainAnalogy,
}