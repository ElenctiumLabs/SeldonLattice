using Microsoft.Extensions.Logging;
using MessagePack;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Infrastructure.Pqc;

/// <summary>
/// IMerkleStore decorator that post-quantum signs every stored node and
/// verifies the signature on read. Signatures are kept as companion blobs
/// (never embedded in the node) so Merkle content-addressing is unaffected.
/// Nodes stored without a signature (e.g. legacy data, or writes through the
/// raw store) are returned as-is; signed nodes that fail verification throw,
/// surfacing tamper evidence to callers.
/// </summary>
public class SignedMerkleStore(
    IMerkleStore inner,
    IPqcSigner signer,
    ILogger<SignedMerkleStore> logger,
    string defaultSigner = "system")
    : IMerkleStore
{
    
    /// <summary>
    /// Stores a node and signs its serialized content.
    /// </summary>
    public async Task<Cid> PutBlobAsync(IMerkleNode node)
    {
        var cid = await inner.PutBlobAsync(node);
        await SignAsync(cid, node.Serialize(), ResolveSigner(node));
        return cid;
    }

    /// <summary>
    /// Retrieves a node by CID and verifies its signature when one is present.
    /// Requires signatures for LlmAuditNode instances.
    /// </summary>
    public async Task<T> GetBlobAsync<T>(Cid cid) where T : IMerkleNode
    {
        var node = await inner.GetBlobAsync<T>(cid);
        var requireSignature = typeof(T) == typeof(LlmAuditNode);
        await VerifyAsync(cid, node.Serialize(), requireSignature);
        return node;
    }

    /// <summary>
    /// Stores a typed object and signs its serialized content.
    /// </summary>
    public async Task<Cid> PutAsync<T>(T obj)
    {
        var payload = MessagePackSerializer.Serialize(obj);
        var cid = Cid.Compute(payload);
        await inner.PutAsync(obj);
        await SignAsync(cid, payload, defaultSigner);
        return cid;
    }

    /// <summary>
    /// Retrieves a typed object by CID and verifies its signature when present.
    /// </summary>
    public async Task<T> GetAsync<T>(Cid cid)
    {
        var obj = await inner.GetAsync<T>(cid);
        var payload = MessagePackSerializer.Serialize(obj);
        await VerifyAsync(cid, payload);
        return obj;
    }

    /// <summary>
    /// Signs the content and persists the companion signature blob.
    /// </summary>
    private async Task SignAsync(Cid cid, byte[] content, string signerId)
    {
        var signature = await signer.SignAsync(signerId, content);
        if (!string.Equals(signature.SignedCid, cid.ToString(), StringComparison.Ordinal))
        {
            signature = signature with { SignedCid = cid.ToString() };
        }
        await inner.PutSignatureAsync(signature);
    }

    /// <summary>
    /// Verifies a node's signature when one exists. Throws on tamper evidence.
    /// </summary>
    private async Task VerifyAsync(Cid cid, byte[] content, bool requireSignature = false)
    {
        var signature = await inner.GetSignatureAsync(cid);
        if (signature is null)
        {
            if (requireSignature)
            {
                throw new InvalidDataException($"Node {cid} is missing a required signature.");
            }
            logger.LogDebug("Node {Cid} is unsigned (legacy or raw store); skipping verification", cid);
            return;
        }

        var valid = await signer.VerifyAsync(signature, content);
        if (!valid)
        {
            throw new InvalidDataException(
                $"Signature verification FAILED for node {cid} (signer {signature.SignerId}). " +
                "Content may have been tampered with.");
        }
    }

    /// <summary>
    /// Resolves the signer identity for a node: the node's SpeakerId for
    /// dialectical nodes and audit nodes, otherwise the configured default signer.
    /// </summary>
    private string ResolveSigner(IMerkleNode node) => node switch
    {
        DialecticalNode d when !string.IsNullOrWhiteSpace(d.SpeakerId) => d.SpeakerId,
        LlmAuditNode a when !string.IsNullOrWhiteSpace(a.SpeakerId) => a.SpeakerId,
        _ => defaultSigner
    };

    public Task<Cid?> GetHeadAsync(string userId) => inner.GetHeadAsync(userId);
    public Task UpdateHeadAsync(string userId, Cid commitCid) => inner.UpdateHeadAsync(userId, commitCid);
    public Task<Cid> CreateCommitAsync(string userId, string message, StateManifest manifest) =>
        inner.CreateCommitAsync(userId, message, manifest);
    public Task<LatticeCommit[]> GetHistoryAsync(string userId, int? limit = null) =>
        inner.GetHistoryAsync(userId, limit);
    public Task<IEnumerable<T>> GetAllNodesAsync<T>() where T : IMerkleNode => inner.GetAllNodesAsync<T>();
    public Task<StoreScan<T>> ScanBlobsAsync<T>(CancellationToken ct = default) where T : IMerkleNode =>
        inner.ScanBlobsAsync<T>(ct);

    public Task<IEnumerable<T>> GetAllAsync<T>() => inner.GetAllAsync<T>();
    public Task PutSignatureAsync(AgentSignature signature) => inner.PutSignatureAsync(signature);
    public Task<AgentSignature?> GetSignatureAsync(Cid signedCid) => inner.GetSignatureAsync(signedCid);
    public IAsyncEnumerable<LlmAuditNode> GetAuditNodesAsync(string sessionId) => inner.GetAuditNodesAsync(sessionId);
    public Task ClearAsync() => inner.ClearAsync();
}
