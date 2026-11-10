using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Infrastructure.Storage;
using Xunit;

namespace SeldonLattice.Application.Tests.Audit;

public class FasterMerkleStoreAuditTests : IDisposable
{
    private readonly string _tempDir;

    public FasterMerkleStoreAuditTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"seldon_audit_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public async Task PutBlobAsync_IndexesAndRetrievesAuditNodesBySessionInTimestampOrder()
    {
        var store = new FasterMerkleStore(_tempDir);
        var sessionId = Guid.NewGuid().ToString();

        var node1 = new LlmAuditNode
        {
            SessionId = sessionId,
            SpeakerId = "proposer",
            CompletionText = "Turn 1",
            Timestamp = new DateTime(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc)
        };

        var node2 = new LlmAuditNode
        {
            SessionId = sessionId,
            SpeakerId = "skeptic",
            CompletionText = "Turn 2",
            Timestamp = new DateTime(2026, 8, 24, 10, 5, 0, DateTimeKind.Utc)
        };

        var otherSessionNode = new LlmAuditNode
        {
            SessionId = "other-session",
            SpeakerId = "mediator",
            CompletionText = "Other session turn",
            Timestamp = new DateTime(2026, 8, 24, 10, 2, 0, DateTimeKind.Utc)
        };

        await store.PutBlobAsync(node2);
        await store.PutBlobAsync(otherSessionNode);
        await store.PutBlobAsync(node1);

        var retrieved = new List<LlmAuditNode>();
        await foreach (var node in store.GetAuditNodesAsync(sessionId))
        {
            retrieved.Add(node);
        }

        Assert.Equal(2, retrieved.Count);
        Assert.Equal("Turn 1", retrieved[0].CompletionText);
        Assert.Equal("Turn 2", retrieved[1].CompletionText);
    }

    [Fact]
    public async Task PutBlobAsync_RoundTripsArgumentClaimNodesScopedBySession()
    {
        var store = new FasterMerkleStore(_tempDir);
        var sessionId = Guid.NewGuid().ToString();

        var claim1 = new ArgumentClaimNode
        {
            SessionId = sessionId,
            Level = 1,
            ClaimText = "A tiered basic income is a single verifiable proposition",
            Confidence = 0.85f,
            SupportedBy = ["turn 3"],
            Attacks = ["free riders"],
            ParentHashes = ["synthesis-cid-1"],
        };
        var claim2 = new ArgumentClaimNode
        {
            SessionId = sessionId,
            Level = 2,
            ClaimText = "Universal coverage needs a different instrument",
            Confidence = 0.7f,
            FalsifiedBy = "#cost-model contradicts",
            ParentHashes = ["synthesis-cid-2"],
        };
        var otherSession = new ArgumentClaimNode
        {
            SessionId = "other-session",
            Level = 9,
            ClaimText = "not ours",
            Confidence = 0.1f,
            ParentHashes = ["other"],
        };

        await store.PutBlobAsync(claim2);
        await store.PutBlobAsync(otherSession);
        await store.PutBlobAsync(claim1);

        var all = await store.GetAllNodesAsync<ArgumentClaimNode>();
        var mine = all.Where(c => c.SessionId == sessionId).OrderBy(c => c.Level).ToList();

        Assert.Equal(2, mine.Count);
        Assert.Equal("A tiered basic income is a single verifiable proposition", mine[0].ClaimText);
        Assert.Equal(new[] { "turn 3" }, mine[0].SupportedBy);
        Assert.Equal(["synthesis-cid-1"], mine[0].ParentHashes);
        Assert.Equal(2, mine[1].Level);
        Assert.Equal("#cost-model contradicts", mine[1].FalsifiedBy);
    }

    [Fact]
    public async Task ClaimNodeCid_IsDeterministicForIdenticalContent()
    {
        var now = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
        var a = new ArgumentClaimNode
        {
            SessionId = "s", Level = 1, ClaimText = "deterministic claim", Confidence = 0.6f,
            ParentHashes = ["cid"], Timestamp = now,
        };
        var b = new ArgumentClaimNode
        {
            SessionId = "s", Level = 1, ClaimText = "deterministic claim", Confidence = 0.6f,
            ParentHashes = ["cid"], Timestamp = now,
        };
        var tampered = b with { Confidence = 0.61f };

        Assert.Equal(a.GetCid(), b.GetCid());
        Assert.NotEqual(a.GetCid(), tampered.GetCid());
    }
}
