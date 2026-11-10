using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Infrastructure.Storage;
using Xunit;

namespace SeldonLattice.Domain.Tests;

public class FasterMerkleStoreTests
{
    [Fact]
    public async Task GetAllNodesAsync_should_ignore_non_object_store_entries()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SeldonLatticeTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new FasterMerkleStore(tempDir);
            var node = new DialecticalNode
            {
                NodeType = "test",
                Level = 0,
                Content = "hello",
                Timestamp = DateTime.UtcNow,
                SessionId = "session-1"
            };
            await store.PutBlobAsync(node);

            // Add a head-like non-object entry into the underlying store
            var headKey = "head:session-1";
            var badBytes = System.Text.Encoding.UTF8.GetBytes("not-msgpack");
            var storeField = typeof(FasterMerkleStore)
                .GetField("_store", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (storeField?.GetValue(store) is System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> dictionary)
            {
                dictionary[headKey] = badBytes;
            }

            var nodes = await store.GetAllNodesAsync<DialecticalNode>();

            Assert.Single(nodes);
            Assert.Equal("test", nodes.First().NodeType);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ManySequentialWrites_ProduceCorrectStateAfterReload()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SeldonLatticeTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            const int count = 500;
            var store = new FasterMerkleStore(tempDir);
            var cids = new List<Cid>();
            for (int i = 0; i < count; i++)
            {
                var node = new DialecticalNode
                {
                    NodeType = "proposer_reply",
                    Level = i,
                    Content = $"Sequential write {i}",
                    Timestamp = DateTime.UtcNow,
                    SessionId = "session-1"
                };
                cids.Add(await store.PutBlobAsync(node));
            }
            await store.DisposeAsync();

            var reloaded = new FasterMerkleStore(tempDir);
            var nodes = await reloaded.GetAllNodesAsync<DialecticalNode>();
            Assert.Equal(count, nodes.Count());

            for (int i = 0; i < count; i++)
            {
                var retrieved = await reloaded.GetBlobAsync<DialecticalNode>(cids[i]);
                Assert.Equal($"Sequential write {i}", retrieved.Content);
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Compaction_FiresAndProducesSmallerFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SeldonLatticeTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // compactionFactor 1 means every append triggers a full rewrite,
            // collapsing the growing append-only log back to live keys only.
            var store = new FasterMerkleStore(tempDir, defaultHistoryLimit: 100, compactionFactor: 1);

            // Same content every time => same CID => same key, so without
            // compaction the log would hold 50 duplicate lines for one key.
            var node = new DialecticalNode
            {
                NodeType = "proposer_reply",
                Level = 0,
                Content = "Compaction target",
                Timestamp = DateTime.UtcNow,
                SessionId = "session-1"
            };
            for (int i = 0; i < 50; i++)
            {
                await store.PutBlobAsync(node);
            }

            var dataFile = Path.Combine(tempDir, "store.dat");
            var lineCount = (await File.ReadAllLinesAsync(dataFile)).Count(l => !string.IsNullOrWhiteSpace(l));

            // Compaction keeps the on-disk file at the live-key count (1),
            // far below the 50 appends that happened.
            Assert.Equal(1, lineCount);

            var reloaded = new FasterMerkleStore(tempDir);
            var nodes = await reloaded.GetAllNodesAsync<DialecticalNode>();
            Assert.Single(nodes);
            Assert.Equal("Compaction target", nodes.First().Content);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Load_IgnoresPartialTrailingLine()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SeldonLatticeTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new FasterMerkleStore(tempDir);
            var node = new DialecticalNode
            {
                NodeType = "proposer_reply",
                Level = 0,
                Content = "Intact before crash",
                Timestamp = DateTime.UtcNow,
                SessionId = "session-1"
            };
            await store.PutBlobAsync(node);
            await store.DisposeAsync();

            // Simulate a crash mid-append: a trailing line with a truncated base64 body.
            var dataFile = Path.Combine(tempDir, "store.dat");
            await File.AppendAllTextAsync(dataFile, $"obj:some-cid|bG9yZW0gaXBzdW0gZG9sb3I=truncated-not-valid{Environment.NewLine}");

            var reloaded = new FasterMerkleStore(tempDir);
            var nodes = await reloaded.GetAllNodesAsync<DialecticalNode>();
            Assert.Single(nodes);
            Assert.Equal("Intact before crash", nodes.First().Content);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
