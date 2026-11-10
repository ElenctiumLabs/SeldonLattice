using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Infrastructure.Storage;
using Xunit;

namespace SeldonLattice.Domain.Tests;

/// <summary>
/// Per-turn quality metrics are now persisted on the node (Keys 35-43) rather than
/// living only on the SSE event and in browser localStorage. These tests pin the two
/// properties that make them usable for downstream analytics: they survive a store
/// round-trip, and "never scored" stays distinguishable from "scored zero".
/// </summary>
public class DialecticalNodeQualityMetricsTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SeldonLatticeQualityMetrics", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task Quality_metrics_survive_a_store_round_trip()
    {
        var tempDir = NewTempDir();
        try
        {
            var node = new DialecticalNode
            {
                NodeType = "skeptic_reply",
                Level = 3,
                Content = "skeptic responds",
                Timestamp = DateTime.UtcNow,
                SessionId = "session-metrics",
                SpeakerId = "skeptic",
                Turn = 2,
                QualityScore = 78.5,
                CeremonyScore = 1.25,
                SimilarityToPrevious = 0.31,
                NewArgumentCount = 4,
                WordCount = 210,
                HasFarewell = false,
                HasAgreement = true,
                HasDisagreement = true,
                SelfSimilarity = 0.12,
            };

            var store = new FasterMerkleStore(tempDir);
            var cid = await store.PutBlobAsync(node);

            // Reopen from disk so this exercises serialisation, not an in-memory cache.
            var reloaded = new FasterMerkleStore(tempDir);
            var retrieved = await reloaded.GetBlobAsync<DialecticalNode>(cid);

            Assert.Equal(78.5, retrieved.QualityScore);
            Assert.Equal(1.25, retrieved.CeremonyScore);
            Assert.Equal(0.31, retrieved.SimilarityToPrevious);
            Assert.Equal(4, retrieved.NewArgumentCount);
            Assert.Equal(210, retrieved.WordCount);
            Assert.False(retrieved.HasFarewell);
            Assert.True(retrieved.HasAgreement);
            Assert.True(retrieved.HasDisagreement);
            Assert.Equal(0.12, retrieved.SelfSimilarity);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task A_measured_zero_is_distinct_from_never_scored()
    {
        // A maximally repetitive reply can legitimately score 0. Null must mean "the
        // gates never ran on this node", because conflating the two makes analytics
        // unable to tell a real zero from an absent measurement.
        var tempDir = NewTempDir();
        try
        {
            var store = new FasterMerkleStore(tempDir);

            var scoredZero = await store.PutBlobAsync(new DialecticalNode
            {
                NodeType = "skeptic_reply",
                Level = 0,
                Content = "restates the question verbatim",
                Timestamp = DateTime.UtcNow,
                SessionId = "session-metrics",
                QualityScore = 0,
                NewArgumentCount = 0,
            });

            // A question node: the gates never run on it, so nothing is recorded.
            var neverScored = await store.PutBlobAsync(new DialecticalNode
            {
                NodeType = "question",
                Level = 0,
                Content = "What is courage?",
                Timestamp = DateTime.UtcNow,
                SessionId = "session-metrics",
            });

            var reloaded = new FasterMerkleStore(tempDir);
            var zero = await reloaded.GetBlobAsync<DialecticalNode>(scoredZero);
            var none = await reloaded.GetBlobAsync<DialecticalNode>(neverScored);

            Assert.Equal(0, zero.QualityScore);
            Assert.NotNull(zero.QualityScore);
            Assert.Null(none.QualityScore);
            Assert.Equal(0, zero.NewArgumentCount);
            Assert.Null(none.NewArgumentCount);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Nodes_without_metrics_do_not_gain_them()
    {
        // Existing callers that never pass metrics must keep writing nulls, so the
        // nullable fields do not silently become zeros on every unrelated node.
        var tempDir = NewTempDir();
        try
        {
            var store = new FasterMerkleStore(tempDir);
            var cid = await store.PutBlobAsync(new DialecticalNode
            {
                NodeType = "question",
                Level = 0,
                Content = "What is courage?",
                Timestamp = DateTime.UtcNow,
                SessionId = "session-metrics",
            });

            var reloaded = new FasterMerkleStore(tempDir);
            var node = await reloaded.GetBlobAsync<DialecticalNode>(cid);

            Assert.Null(node.QualityScore);
            Assert.Null(node.CeremonyScore);
            Assert.Null(node.WordCount);
            Assert.Null(node.HasAgreement);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
