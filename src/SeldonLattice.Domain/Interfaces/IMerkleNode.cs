using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Interface for Merkle DAG nodes that can compute their content identifier and serialize themselves.
/// </summary>
public interface IMerkleNode
{
    /// <summary>
    /// Computes the content identifier for this node based on its current state.
    /// </summary>
    Cid GetCid();

    /// <summary>
    /// Serializes the node to bytes for storage.
    /// </summary>
    byte[] Serialize();
}