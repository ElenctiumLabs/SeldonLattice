using MessagePack;
using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.Domain.Models.Storage;

/// <summary>
/// A commit in the Seldon Lattice representing a snapshot of the lattice state.
/// Forms a Merkle DAG chain with optional parent commits.
/// </summary>
[MessagePackObject]
public record LatticeCommit : IMerkleNode
{
    /// <summary>
    /// CID of the state manifest for this commit.
    /// </summary>
    [Key(0)] public string RootManifestCid { get; init; } = "";

    /// <summary>
    /// Optional CID of the parent commit for branching history.
    /// </summary>
    [Key(1)] public string? ParentCommitCid { get; init; }

    /// <summary>
    /// When this commit was created.
    /// </summary>
    [Key(2)] public DateTime Timestamp { get; init; }

    /// <summary>
    /// Commit message describing the changes.
    /// </summary>
    [Key(3)] public string Message { get; init; } = "";

    /// <summary>
    /// The user who created this commit.
    /// </summary>
    [Key(4)] public string UserId { get; init; } = "";

    public Cid GetCid() => Cid.Compute(Serialize());
    public byte[] Serialize() => MessagePackSerializer.Serialize(this);
}