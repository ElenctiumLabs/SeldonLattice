using MessagePack;
using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.Domain.Models.Storage;

/// <summary>
/// Manifest representing the complete state of the lattice at a point in time.
/// Tracks all nodes, clusters, and tags in the knowledge graph.
/// </summary>
[MessagePackObject]
public record StateManifest : IMerkleNode
{
    /// <summary>
    /// CIDs of all nodes in the lattice.
    /// </summary>
    [Key(0)] public string[] NodeCids { get; init; } = [];

    /// <summary>
    /// CIDs of all clusters in the lattice.
    /// </summary>
    [Key(1)] public string[] ClusterCids { get; init; } = [];

    /// <summary>
    /// CIDs of all semantic tags in the lattice.
    /// </summary>
    [Key(2)] public string[] TagCids { get; init; } = [];

    /// <summary>
    /// Total count of nodes in the lattice.
    /// </summary>
    [Key(3)] public int TotalNodeCount { get; init; }

    /// <summary>
    /// Total count of clusters in the lattice.
    /// </summary>
    [Key(4)] public int TotalClusterCount { get; init; }

    /// <summary>
    /// The user who created this manifest.
    /// </summary>
    [Key(5)] public string UserId { get; init; } = "";

    public Cid GetCid() => Cid.Compute(Serialize());
    public byte[] Serialize() => MessagePackSerializer.Serialize(this);
}