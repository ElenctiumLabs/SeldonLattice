using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.Domain.Models.Storage;

/// <summary>
/// A Merkle node together with the content identifier it is filed under in the store.
/// <para>
/// The key CID is the identifier the node was written with, before any later read.
/// Verifiers compare it against <see cref="IMerkleNode.GetCid"/> recomputed from the
/// node's current bytes; when the two differ, the stored content no longer matches the
/// identifier it is filed under, which is what substitution looks like on disk.
/// </para>
/// </summary>
/// <typeparam name="T">Concrete node type.</typeparam>
public sealed record StoredNode<T>(Cid KeyCid, T Node) where T : IMerkleNode;

/// <summary>
/// Result of scanning the store for nodes of a given type.
/// <para>
/// <see cref="UnreadableKeys"/> lists object keys whose payload could not be decoded into
/// a node. A store scan that silently dropped these would report a damaged session as clean,
/// so verification treats every unreadable key as an integrity failure.
/// </para>
/// </summary>
/// <typeparam name="T">Concrete node type.</typeparam>
public sealed record StoreScan<T>(
    IReadOnlyList<StoredNode<T>> Nodes,
    IReadOnlyList<string> UnreadableKeys) where T : IMerkleNode
{
    /// <summary>An empty scan.</summary>
    public static StoreScan<T> Empty { get; } = new([], []);
}