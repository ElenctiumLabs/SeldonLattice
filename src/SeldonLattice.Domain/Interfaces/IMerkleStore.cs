using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Merkle DAG-based storage interface for immutable, content-addressable data.
/// Uses CIDs (Content Identifiers) to ensure data integrity and enable versioning.
/// </summary>
public interface IMerkleStore
{
    /// <summary>
    /// Stores a Merkle node as a blob with type information preserved.
    /// </summary>
    Task<Cid> PutBlobAsync(IMerkleNode node);

    /// <summary>
    /// Retrieves a typed node from storage by its CID.
    /// </summary>
    Task<T> GetBlobAsync<T>(Cid cid) where T : IMerkleNode;

    /// <summary>
    /// Stores a typed object and returns its computed CID.
    /// </summary>
    Task<Cid> PutAsync<T>(T obj);

    /// <summary>
    /// Retrieves a typed object by its CID.
    /// </summary>
    Task<T> GetAsync<T>(Cid cid);

    /// <summary>
    /// Gets the current head commit CID for a user.
    /// </summary>
    Task<Cid?> GetHeadAsync(string userId);

    /// <summary>
    /// Updates the head commit CID for a user.
    /// </summary>
    Task UpdateHeadAsync(string userId, Cid commitCid);

    /// <summary>
    /// Creates a new commit with the given manifest.
    /// </summary>
    Task<Cid> CreateCommitAsync(string userId, string message, StateManifest manifest);

    /// <summary>
    /// Gets the commit history for a user.
    /// </summary>
    Task<LatticeCommit[]> GetHistoryAsync(string userId, int? limit = null);

    /// <summary>
    /// Gets all nodes of a specific type.
    /// </summary>
    /// <remarks>
    /// Convenience projection over <see cref="ScanBlobsAsync{T}"/>. It discards the key a
    /// node is filed under and any payload that failed to decode, so verification must use
    /// <see cref="ScanBlobsAsync{T}"/> instead.
    /// </remarks>
    Task<IEnumerable<T>> GetAllNodesAsync<T>() where T : IMerkleNode;

    /// <summary>
    /// Scans the store for nodes of a specific type, returning each node together with the
    /// content identifier it is filed under, plus the keys whose payloads could not be decoded.
    /// </summary>
    /// <typeparam name="T">Concrete node type to project.</typeparam>
    /// <param name="ct">Cancellation token.</param>
    Task<StoreScan<T>> ScanBlobsAsync<T>(CancellationToken ct = default) where T : IMerkleNode;


    /// <summary>
    /// Gets all stored typed objects.
    /// </summary>
    Task<IEnumerable<T>> GetAllAsync<T>();

    /// <summary>
    /// Persists a companion signature blob for a node, retrievable by the signed node's CID.
    /// </summary>
    Task PutSignatureAsync(AgentSignature signature);

    /// <summary>
    /// Retrieves the companion signature for a node, if one exists.
    /// </summary>
    Task<AgentSignature?> GetSignatureAsync(Cid signedCid);

    /// <summary>
    /// Returns all <see cref="LlmAuditNode"/> records for the given session,
    /// yielding results in <c>Timestamp</c> ascending order.
    /// Uses a secondary session index for sub-second performance; falls back
    /// to a full node scan with a structured warning when the index is absent or corrupt.
    /// </summary>
    IAsyncEnumerable<LlmAuditNode> GetAuditNodesAsync(string sessionId);

    /// <summary>
    /// Clears all data from the store, removing all persisted nodes, heads, and signatures.
    /// </summary>
    Task ClearAsync();
}