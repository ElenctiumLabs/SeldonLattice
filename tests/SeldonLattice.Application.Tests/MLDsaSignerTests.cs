using MessagePack;
using Microsoft.Extensions.Logging.Abstractions;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Infrastructure.Pqc;
using Xunit;

namespace SeldonLattice.Application.Tests;

public class MLDsaSignerTests
{
    private static string TempKeyDir() =>
        Path.Combine(Path.GetTempPath(), "seldon-pqc", Guid.NewGuid().ToString("N"));

    private static MLDsaSigner CreateSigner(string keyDir) =>
        new(keyDir, NullLogger<MLDsaSigner>.Instance);

    [Fact]
    public async Task SignAsync_ProducesCompletesSelfVerifyingSignature()
    {
        var signer = CreateSigner(TempKeyDir());
        var content = "philosophical synthesis content"u8.ToArray();

        var signature = await signer.SignAsync("proposer", content);

        Assert.True(signature.IsComplete);
        Assert.Equal("ML-DSA-65", signature.Algorithm);
        Assert.Equal("proposer", signature.SignerId);
        Assert.NotEmpty(signature.PublicKey);
        Assert.NotEmpty(signature.Signature);
        Assert.StartsWith("sha3-256-", signature.SignedCid);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsTrue_ForGenuineSignature()
    {
        var signer = CreateSigner(TempKeyDir());
        var content = "genuine content"u8.ToArray();

        var signature = await signer.SignAsync("skeptic", content);

        Assert.True(await signer.VerifyAsync(signature, content));
    }

    [Fact]
    public async Task VerifyAsync_ReturnsFalse_ForTamperedContent()
    {
        var signer = CreateSigner(TempKeyDir());
        var original = "original content"u8.ToArray();
        var tampered = "tampered content!"u8.ToArray();

        var signature = await signer.SignAsync("mediator", original);

        Assert.False(await signer.VerifyAsync(signature, tampered));
    }

    [Fact]
    public async Task VerifyAsync_ReturnsFalse_ForInvalidSignatureBytes()
    {
        var signer = CreateSigner(TempKeyDir());
        var content = "content"u8.ToArray();

        var signature = await signer.SignAsync("proposer", content);
        var corrupted = signature with { Signature = Convert.ToBase64String(new byte[64]) };

        Assert.False(await signer.VerifyAsync(corrupted, content));
    }

    [Fact]
    public async Task SignerId_KeyIsStable_AcrossSignerInstances()
    {
        var keyDir = TempKeyDir();
        var first = CreateSigner(keyDir);
        var second = CreateSigner(keyDir);
        var content = "stable key"u8.ToArray();

        var sig1 = await first.SignAsync("skeptic", content);
        var sig2 = await second.SignAsync("skeptic", content);

        Assert.Equal(sig1.PublicKey, sig2.PublicKey);
    }

    [Fact]
    public async Task Signature_Verifies_AcrossSignerInstances()
    {
        var keyDir = TempKeyDir();
        var first = CreateSigner(keyDir);
        var second = CreateSigner(keyDir);
        var content = "cross-instance"u8.ToArray();

        var signature = await first.SignAsync("mediator", content);

        Assert.True(await second.VerifyAsync(signature, content));
    }

    [Fact]
    public async Task DifferentSignerIds_UseDifferentKeys()
    {
        var signer = CreateSigner(TempKeyDir());
        var content = "different keys"u8.ToArray();

        var sigA = await signer.SignAsync("proposer", content);
        var sigB = await signer.SignAsync("mediator", content);

        Assert.NotEqual(sigA.PublicKey, sigB.PublicKey);
    }

    [Fact]
    public async Task Signature_IsSelfVerifying_FromPublicKeyAlone()
    {
        var signer = CreateSigner(TempKeyDir());
        var content = "audit trail"u8.ToArray();

        var signature = await signer.SignAsync("system", content);

        // A verifier that only sees the signature record (public key embedded)
        // must be able to authenticate the content without any key store.
        var verifier = CreateSigner(TempKeyDir());
        Assert.True(await verifier.VerifyAsync(signature, content));
    }

    [Fact]
    public async Task RoundTrips_ThroughMessagePack_StillVerify()
    {
        var signer = CreateSigner(TempKeyDir());
        var content = "serializable signature"u8.ToArray();

        var signature = await signer.SignAsync("mediator", content);
        var bytes = MessagePackSerializer.Serialize(signature);
        var deserialized = MessagePackSerializer.Deserialize<AgentSignature>(bytes);

        Assert.True(await signer.VerifyAsync(deserialized, content));
    }
}