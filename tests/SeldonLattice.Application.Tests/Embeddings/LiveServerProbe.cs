using Microsoft.Extensions.Logging;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Infrastructure.Services;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable SKEXP0001

namespace SeldonLattice.Application.Tests.Embeddings;

/// <summary>
/// Live probe against a running llama.cpp embedding server. Skipped unless
/// SELDON_LIVE_EMBED_URL is set, so CI stays hermetic.
///
/// Two directions, because either alone is misleading:
///
///   PASS - the reference currently on disk must AGREE with the live server when the stored
///          corpus is in the same space as that server. If it does not, either the reference
///          is stale or the server's space changed; both need a human.
///   FAIL - a reference from a DIFFERENT space must be DETECTED. A guard that cannot fail is
///          worse than no guard, so this direction is what proves the check is live.
///
/// The second is driven by a synthetic reference (the live vector rotated), not by an old
/// artifact, so the test stays meaningful however the on-disk reference changes over time.
/// </summary>
public class LiveServerProbe(ITestOutputHelper output)
{
    private static string? Url => Environment.GetEnvironmentVariable("SELDON_LIVE_EMBED_URL");

    /// <summary>
    /// The reference on disk must agree with the live server. This is the ordinary healthy
    /// state: the corpus was built by this server, so a reference captured from it matches.
    /// </summary>
    [Fact]
    public async Task ExistingReference_AgreesWithTheLiveServer()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            output.WriteLine("SELDON_LIVE_EMBED_URL not set; skipping live probe.");
            return;
        }

        var refPath = Environment.GetEnvironmentVariable("SELDON_SENTINEL_REF")
                      ?? "embedding_sentinel.json";
        var sentinelText = Environment.GetEnvironmentVariable("SELDON_SENTINEL_TEXT")
                           ?? "What is philosophy??";

        var opts = new EmbeddingValidationOptions
        {
            ExpectedDimension = 768,
            ExpectedL2Norm = 1.0f,
            ThrowOnShapeFailure = true,
            SentinelText = sentinelText,
            SentinelReferencePath = refPath,
            MinSentinelCosine = 0.99f,
            ThrowOnContinuityFailure = true,
        };

        var store = new FileEmbeddingSentinelStore(
            new EmptyMerkleStore(), opts,
            LoggerFactory.Create(b => b.AddProvider(new XunitLoggerProvider(output)))
                .CreateLogger<FileEmbeddingSentinelStore>());

        if (!await store.HasReferenceAsync())
        {
            output.WriteLine($"No reference at {refPath}; skipping the agreement probe.");
            return;
        }

        var svc = new OpenAICompatibleEmbeddingService(
            new HttpClient { Timeout = TimeSpan.FromMinutes(3) },
            Url!, "nomic-embed-text-v1.5", "key",
            LoggerFactory.Create(b => b.AddProvider(new XunitLoggerProvider(output)))
                .CreateLogger<OpenAICompatibleEmbeddingService>(),
            opts, store);

        // Must NOT throw: the reference describes the space the live server is producing in.
        var result = await svc.GenerateEmbeddingsAsync([sentinelText]);
        Assert.Single(result);
        output.WriteLine("Reference agrees with the live server (no continuity failure raised).");
    }

    /// <summary>
    /// A reference from a different space must be detected. Proves the check is live rather
    /// than passing for want of ever being able to fail.
    /// </summary>
    [Fact]
    public async Task ReferenceFromADifferentSpace_IsDetected()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            output.WriteLine("SELDON_LIVE_EMBED_URL not set; skipping live probe.");
            return;
        }

        var sentinelText = Environment.GetEnvironmentVariable("SELDON_SENTINEL_TEXT")
                           ?? "What is philosophy??";

        var probe = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

        // Fetch the live sentinel vector, then build a reference in a demonstrably different
        // space: same dimension, unit norm, but an orthogonal-ish rotation. Shape checks pass;
        // only continuity can catch it.
        var liveService = new OpenAICompatibleEmbeddingService(
            probe, Url!, "nomic-embed-text-v1.5", "key",
            LoggerFactory.Create(b => b.AddProvider(new XunitLoggerProvider(output)))
                .CreateLogger<OpenAICompatibleEmbeddingService>(),
            new EmbeddingValidationOptions { Enabled = false });
        var live = (await liveService.GenerateEmbeddingsAsync([sentinelText]))[0].ToArray();

        var otherSpace = new float[live.Length];
        for (var i = 0; i < live.Length; i++)
            otherSpace[i] = live[(i + 37) % live.Length] * (i % 2 == 0 ? 1f : -1f);

        var opts = new EmbeddingValidationOptions
        {
            ExpectedDimension = 768,
            ExpectedL2Norm = 1.0f,
            ThrowOnShapeFailure = true,
            SentinelText = sentinelText,
            SentinelReference = otherSpace,     // inline: no file, no store needed
            MinSentinelCosine = 0.99f,
            ThrowOnContinuityFailure = true,
        };

        var svc = new OpenAICompatibleEmbeddingService(
            probe, Url!, "nomic-embed-text-v1.5", "key",
            LoggerFactory.Create(b => b.AddProvider(new XunitLoggerProvider(output)))
                .CreateLogger<OpenAICompatibleEmbeddingService>(),
            opts);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GenerateEmbeddingsAsync([sentinelText]));

        output.WriteLine(ex.Message);
        Assert.Contains("continuity check FAILED", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DIFFERENT SPACE", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Stand-in for the merkle store: reports no nodes, so <c>IsCorpusEmptyAsync</c> returns
    /// true. Only reached when the reference file is absent - the probe's point is the
    /// comparison against an existing reference.
    /// </summary>
    private sealed class EmptyMerkleStore : IMerkleStore
    {
        public Task<Cid> PutBlobAsync(
            IMerkleNode node) => throw new NotSupportedException();
        public Task<T> GetBlobAsync<T>(Cid cid) where T : IMerkleNode => throw new NotSupportedException();
        public Task<Cid> PutAsync<T>(T obj) => throw new NotSupportedException();
        public Task<T> GetAsync<T>(Cid cid) => throw new NotSupportedException();
        public Task<Cid?> GetHeadAsync(string userId) => throw new NotSupportedException();
        public Task UpdateHeadAsync(string userId, Cid commitCid) => throw new NotSupportedException();
        public Task<Cid> CreateCommitAsync(string userId, string message, StateManifest manifest) => throw new NotSupportedException();
        public Task<LatticeCommit[]> GetHistoryAsync(string userId, int? limit = null) => throw new NotSupportedException();
        public Task<IEnumerable<T>> GetAllNodesAsync<T>() where T : IMerkleNode
            => Task.FromResult(Enumerable.Empty<T>());

        public Task<StoreScan<T>> ScanBlobsAsync<T>(CancellationToken ct = default) where T : IMerkleNode
            => throw new NotImplementedException();

        public Task<IEnumerable<T>> GetAllAsync<T>() => Task.FromResult(Enumerable.Empty<T>());
        public Task PutSignatureAsync(AgentSignature signature) => throw new NotSupportedException();
        public Task<AgentSignature?> GetSignatureAsync(Cid signedCid) => throw new NotSupportedException();
        public IAsyncEnumerable<LlmAuditNode> GetAuditNodesAsync(string sessionId)
            => throw new NotSupportedException();
        public Task ClearAsync() => throw new NotSupportedException();
    }

    private sealed class XunitLoggerProvider(ITestOutputHelper o) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new L(o, categoryName);
        public void Dispose() { }
        private sealed class L(ITestOutputHelper o, string cat) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState s) where TState : notnull => null;
            public bool IsEnabled(LogLevel l) => true;
            public void Log<TState>(LogLevel l, EventId e, TState s, Exception? ex,
                Func<TState, Exception?, string> f) => o.WriteLine($"[{l}] {cat}: {f(s, ex)}");
        }
    }
}
