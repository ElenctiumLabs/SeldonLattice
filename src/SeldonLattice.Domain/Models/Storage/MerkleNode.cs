using MessagePack;
using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.Domain.Models.Storage;

/// <summary>
/// A node in the Seldon Lattice representing a philosophical insight or proposition.
/// Identified by its semantic coordinates and relationships to other nodes.
/// </summary>
[MessagePackObject]
public record MerkleNode : IMerkleNode
{
    /// <summary>
    /// Unique identifier for this node.
    /// </summary>
    [Key(0)] public string Id { get; init; } = "";

    /// <summary>
    /// Semantic coordinates in the embedding space.
    /// </summary>
    [Key(1)] public float[] Coordinates { get; init; } = [];

    /// <summary>
    /// IDs of semantic tags attached to this node.
    /// </summary>
    [Key(2)] public string[] SemanticTagIds { get; init; } = [];

    /// <summary>
    /// IDs of edges connected to this node.
    /// </summary>
    [Key(3)] public string[] EdgeIds { get; init; } = [];

    /// <summary>
    /// Version number for optimistic concurrency control.
    /// </summary>
    [Key(4)] public ulong Version { get; init; }

    /// <summary>
    /// Computes the CID by hashing the serialized node.
    /// </summary>
    public Cid GetCid() => Cid.Compute(Serialize());

    /// <summary>
    /// Serializes using MessagePack for efficient binary storage.
    /// </summary>
    public byte[] Serialize() => MessagePackSerializer.Serialize(this);
}