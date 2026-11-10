using MessagePack;

namespace SeldonLattice.Domain.Models.Storage;

/// <summary>
/// Post-quantum digital signature attached to a Merkle node.
/// Covers the node's serialized content bytes so an auditor can verify
/// both who produced the content and that the content is untampered.
/// Stored as a companion blob keyed by the signed node's CID (never
/// embedded in the node itself, which would make content hashing circular).
/// </summary>
[MessagePackObject]
public record AgentSignature
{
    /// <summary>
    /// The signature scheme used, e.g. "ML-DSA-65".
    /// </summary>
    [Key(0)] public string Algorithm { get; init; } = string.Empty;

    /// <summary>
    /// The signing agent, e.g. "mediator_synthesis", "mediator", "system".
    /// </summary>
    [Key(1)] public string SignerId { get; init; } = string.Empty;

    /// <summary>
    /// Base64-encoded public key so the signature is self-verifying.
    /// </summary>
    [Key(2)] public string PublicKey { get; init; } = string.Empty;

    /// <summary>
    /// Base64-encoded signature bytes.
    /// </summary>
    [Key(3)] public string Signature { get; init; } = string.Empty;

    /// <summary>
    /// CID of the signed node, for cross-referencing the companion blob.
    /// </summary>
    [Key(4)] public string SignedCid { get; init; } = string.Empty;

    /// <summary>
    /// When the signature was produced.
    /// </summary>
    [Key(5)] public DateTime Timestamp { get; init; }

    /// <summary>
    /// True when all fields required for verification are populated.
    /// </summary>
    [IgnoreMember]
    public bool IsComplete =>
        !string.IsNullOrEmpty(Algorithm) &&
        !string.IsNullOrEmpty(PublicKey) &&
        !string.IsNullOrEmpty(Signature) &&
        !string.IsNullOrEmpty(SignedCid);
}