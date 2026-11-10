using MessagePack;
using SeldonLattice.Domain.Models.Storage;
using Xunit;

namespace SeldonLattice.Domain.Tests;

public class AgentSignatureTests
{
    [Fact]
    public void IsComplete_IsTrue_WhenAllFieldsPopulated()
    {
        var signature = new AgentSignature
        {
            Algorithm = "ML-DSA-65",
            SignerId = "mediator",
            PublicKey = "abc",
            Signature = "def",
            SignedCid = "sha3-256-xyz",
            Timestamp = DateTime.UtcNow
        };

        Assert.True(signature.IsComplete);
    }

    [Fact]
    public void IsComplete_IsFalse_WhenRequiredFieldMissing()
    {
        var signature = new AgentSignature
        {
            Algorithm = "ML-DSA-65",
            SignerId = "mediator",
            SignedCid = "sha3-256-xyz"
        };

        Assert.False(signature.IsComplete);
    }

    [Fact]
    public void RoundTrips_ThroughMessagePack()
    {
        var signature = new AgentSignature
        {
            Algorithm = "ML-DSA-65",
            SignerId = "proposer",
            PublicKey = "abc",
            Signature = "def",
            SignedCid = "sha3-256-xyz",
            Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var bytes = MessagePackSerializer.Serialize(signature);
        var restored = MessagePackSerializer.Deserialize<AgentSignature>(bytes);

        Assert.Equal(signature.Algorithm, restored.Algorithm);
        Assert.Equal(signature.SignerId, restored.SignerId);
        Assert.Equal(signature.PublicKey, restored.PublicKey);
        Assert.Equal(signature.Signature, restored.Signature);
        Assert.Equal(signature.SignedCid, restored.SignedCid);
        Assert.Equal(signature.Timestamp, restored.Timestamp);
        Assert.True(restored.IsComplete);
    }
}