using MessagePack;

namespace SeldonLattice.Domain.Models.Agents;

/// <summary>
/// Result of a mediator's analysis of dialectical tension.
/// Contains the mediator's bridging insight and information about the tension.
///</summary>
[MessagePackObject]
public record MediationContext(
    [property: Key(0)]
    string MediatorAInsight,

    [property: Key(1)]
    string MediatorBInsight,

    [property: Key(2)]
    float TensionIntensity,

    [property: Key(3)]
    string[] UnresolvedTensions
);