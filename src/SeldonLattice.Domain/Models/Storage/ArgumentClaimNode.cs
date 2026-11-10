using MessagePack;
using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.Domain.Models.Storage;

/// <summary>
/// A single atomic, machine-readable claim extracted from a synthesis (Reasoning
/// Integrity Layer, substrate S2). Each claim carries its support provenance, the
/// material it attacks/preempts, an explicit falsification clause, and a bounded
/// confidence. Claim nodes are the load-bearing structure that downstream features -
/// refutation, retraction, cross-session memory, training-data hygiene, verify blocks -
/// attach to. Stored through the same signed Merkle spine as every other node, so each
/// claim is content-addressed and ML-DSA signed like <see cref="DialecticalNode"/>.
/// </summary>
[MessagePackObject]
public record ArgumentClaimNode : IMerkleNode
{
    [Key(0)] public string NodeType { get; init; } = "argument_claim";

    /// <summary>The session this claim was produced in.</summary>
    [Key(1)] public string SessionId { get; init; } = string.Empty;

    /// <summary>The ascent level whose synthesis produced this claim.</summary>
    [Key(2)] public int Level { get; init; }

    /// <summary>The atomic assertion - a single verifiable proposition.</summary>
    [Key(3)] public string ClaimText { get; init; } = string.Empty;

    /// <summary>Bounded confidence in [0, 1]. Claim strength must never exceed the
    /// evidence in its support set (R3 - proportional).</summary>
    [Key(4)] public float Confidence { get; init; }

    /// <summary>A concrete observation or counterexample that would refute the claim
    /// (R2 - refutable). "My opponent disagrees" is not a valid falsification clause.</summary>
    [Key(5)] public string? FalsifiedBy { get; init; }

    /// <summary>Source CIDs the claim is grounded in. Populated by the producer;
    /// the synthesised node that carried the claim is always joined via
    /// <see cref="ParentHashes"/> when available.</summary>
    [Key(6)] public string[] SupportedBy { get; init; } = [];

    /// <summary>Antithesis claim CIDs this claim addresses/preempts.</summary>
    [Key(7)] public string[] Attacks { get; init; } = [];

    /// <summary>Merkle parents - the synthesis node CID(s) the claim came from.</summary>
    [Key(8)] public string[] ParentHashes { get; init; } = [];

    /// <summary>Raw `support_from` labels as emitted by the model (turn/source ids),
    /// kept verbatim for provenance even when they cannot be resolved to CIDs yet.</summary>
    [Key(9)] public string[]? SupportFromLabels { get; init; }

    /// <summary>When this claim was created.</summary>
    [Key(10)] public DateTime Timestamp { get; init; }

    public Cid GetCid() => Cid.Compute(Serialize());

    public byte[] Serialize() => MessagePackSerializer.Serialize(this);
}