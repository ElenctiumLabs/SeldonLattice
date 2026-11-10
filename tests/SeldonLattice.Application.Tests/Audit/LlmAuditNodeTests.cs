using MessagePack;
using SeldonLattice.Domain.Models.Storage;
using Xunit;

namespace SeldonLattice.Application.Tests.Audit;

public class LlmAuditNodeTests
{
    [Fact]
    public void LlmAuditNode_Serialization_RoundTripsAccurately()
    {
        var node = new LlmAuditNode
        {
            NodeType = "llm_audit",
            SessionId = "session-123",
            SpeakerId = "proposer",
            ModuleKey = "philosophy",
            Level = 2,
            Turn = 5,
            PromptMessages = ["system: you are the Proposer", "user: what is justice?"],
            CompletionText = "Justice is harmony in the soul.",
            InputTokenCount = 42,
            OutputTokenCount = 15,
            ModelId = "gpt-4o",
            LatencyMs = 350,
            Timestamp = DateTime.UtcNow,
            ParentCid = "cid-parent-456",
            QualityFlags = ["circular_reasoning"],
            IsFlagged = true
        };

        var bytes = node.Serialize();
        var deserialized = MessagePackSerializer.Deserialize<LlmAuditNode>(bytes);

        Assert.Equal(node.NodeType, deserialized.NodeType);
        Assert.Equal(node.SessionId, deserialized.SessionId);
        Assert.Equal(node.SpeakerId, deserialized.SpeakerId);
        Assert.Equal(node.ModuleKey, deserialized.ModuleKey);
        Assert.Equal(node.Level, deserialized.Level);
        Assert.Equal(node.Turn, deserialized.Turn);
        Assert.Equal(node.PromptMessages, deserialized.PromptMessages);
        Assert.Equal(node.CompletionText, deserialized.CompletionText);
        Assert.Equal(node.InputTokenCount, deserialized.InputTokenCount);
        Assert.Equal(node.OutputTokenCount, deserialized.OutputTokenCount);
        Assert.Equal(node.ModelId, deserialized.ModelId);
        Assert.Equal(node.LatencyMs, deserialized.LatencyMs);
        Assert.Equal(node.ParentCid, deserialized.ParentCid);
        Assert.Equal(node.QualityFlags, deserialized.QualityFlags);
        Assert.Equal(node.IsFlagged, deserialized.IsFlagged);
    }

    [Fact]
    public void LlmAuditNode_GetCid_IsDeterministic()
    {
        var timestamp = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
        var node1 = new LlmAuditNode
        {
            SessionId = "session-1",
            SpeakerId = "skeptic",
            ModuleKey = "philosophy",
            CompletionText = "Virtue is a mean.",
            Timestamp = timestamp
        };

        var node2 = new LlmAuditNode
        {
            SessionId = "session-1",
            SpeakerId = "skeptic",
            ModuleKey = "philosophy",
            CompletionText = "Virtue is a mean.",
            Timestamp = timestamp
        };

        var cid1 = node1.GetCid();
        var cid2 = node2.GetCid();

        Assert.Equal(cid1, cid2);
        Assert.Equal(cid1.ToString(), cid2.ToString());
    }
}
