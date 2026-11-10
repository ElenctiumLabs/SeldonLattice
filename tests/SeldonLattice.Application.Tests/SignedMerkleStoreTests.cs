using Microsoft.Extensions.Logging.Abstractions;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Infrastructure.Pqc;
using SeldonLattice.Infrastructure.Storage;
using Xunit;

namespace SeldonLattice.Application.Tests;

public class SignedMerkleStoreTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "seldon-signed", Guid.NewGuid().ToString("N"));

    private static (SignedMerkleStore signed, FasterMerkleStore raw, MLDsaSigner signer) CreateStore()
    {
        var baseDir = TempDir();
        var raw = new FasterMerkleStore(baseDir);
        var signer = new MLDsaSigner(Path.Combine(baseDir, "keys"), NullLogger<MLDsaSigner>.Instance);
        var signed = new SignedMerkleStore(raw, signer, NullLogger<SignedMerkleStore>.Instance);
        return (signed, raw, signer);
    }

    private static DialecticalNode MakeNode(string content, string speaker) =>
        new()
        {
            NodeType = "socratic_synthesis",
            Content = content,
            SpeakerId = speaker,
            Timestamp = DateTime.UtcNow
        };

    [Fact]
    public async Task PutBlobAsync_Then_GetBlobAsync_VerifiesSignature()
    {
        var (signed, _, _) = CreateStore();
        var node = MakeNode("a verified synthesis", "mediator");

        var cid = await signed.PutBlobAsync(node);
        var loaded = await signed.GetBlobAsync<DialecticalNode>(cid);

        Assert.Equal(node.Content, loaded.Content);
        Assert.Equal(node.SpeakerId, loaded.SpeakerId);
    }

    [Fact]
    public async Task Signature_IsPersisted_WithSignedCidKeyedToNode()
    {
        var (signed, raw, _) = CreateStore();
        var node = MakeNode("signed by proposer", "proposer");

        var cid = await signed.PutBlobAsync(node);
        var signature = await raw.GetSignatureAsync(cid);

        Assert.NotNull(signature);
        Assert.Equal(cid.ToString(), signature!.SignedCid);
        Assert.Equal("proposer", signature.SignerId);
        Assert.Equal("ML-DSA-65", signature.Algorithm);
    }

    [Fact]
    public async Task NonDialecticalNodes_SignWithDefaultSigner()
    {
        var (signed, raw, _) = CreateStore();
        var commit = new LatticeCommit
        {
            Message = "system commit",
            Timestamp = DateTime.UtcNow,
            UserId = "test-user"
        };

        var cid = await signed.PutBlobAsync(commit);
        var signature = await raw.GetSignatureAsync(cid);

        Assert.NotNull(signature);
        Assert.Equal("system", signature!.SignerId);
    }

    [Fact]
    public async Task GetBlobAsync_Throws_WhenSignatureDoesNotMatchContent()
    {
        var (signed, raw, signer) = CreateStore();
        var node = MakeNode("original truth", "mediator");

        var cid = await signed.PutBlobAsync(node);

        // Simulate tampering: replace the companion signature with a valid
        // signature over different content, forcing verification to fail.
        var forged = await signer.SignAsync("mediator", [.. "forged content"u8]);
        await raw.PutSignatureAsync(forged with { SignedCid = cid.ToString() });

        await Assert.ThrowsAsync<InvalidDataException>(() => signed.GetBlobAsync<DialecticalNode>(cid));
    }

    [Fact]
    public async Task GetBlobAsync_ReturnsNode_WhenSignatureMissing()
    {
        var (signed, raw, _) = CreateStore();
        var node = MakeNode("legacy unsigned node", "proposer");

        var cid = await raw.PutBlobAsync(node); // bypass signing path

        var loaded = await signed.GetBlobAsync<DialecticalNode>(cid);
        Assert.Equal(node.Content, loaded.Content);
    }

    [Fact]
    public async Task PutAsync_Then_GetAsync_VerifiesGenericObjects()
    {
        var (signed, _, _) = CreateStore();
        var session = new Session
        {
            SessionId = "s-1",
            RootQuestion = "what is just?",
            CreatedAt = DateTime.UtcNow
        };

        var cid = await signed.PutAsync(session);
        var loaded = await signed.GetAsync<Session>(cid);

        Assert.Equal(session.RootQuestion, loaded.RootQuestion);
    }

    [Fact]
    public async Task GetAllNodesAsync_ReturnsSignedNodes()
    {
        var (signed, _, _) = CreateStore();
        await signed.PutBlobAsync(MakeNode("one", "proposer"));
        await signed.PutBlobAsync(MakeNode("two", "skeptic"));

        var nodes = (await signed.GetAllNodesAsync<DialecticalNode>()).ToList();

        Assert.Equal(2, nodes.Count);
        Assert.Contains(nodes, n => n.Content == "one");
        Assert.Contains(nodes, n => n.Content == "two");
    }
}