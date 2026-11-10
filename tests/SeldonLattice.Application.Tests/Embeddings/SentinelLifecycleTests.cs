using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SeldonLattice.Infrastructure.Services;
using Xunit;

#pragma warning disable SKEXP0001

namespace SeldonLattice.Application.Tests.Embeddings;

/// <summary>
/// Tests for the continuity reference LIFECYCLE, which is a different question from whether
/// the cosine comparison works (covered by <see cref="EmbeddingValidationTests"/>).
///
/// The lifecycle exists because a guard that cannot fail is worse than no guard. The failure
/// it prevents: a reference captured from the LIVE SERVER compares the space against itself
/// and passes trivially, so the check reports "passed" while measuring nothing. That is exactly
/// what had happened - the committed reference was bit-identical to the server's output.
///
/// Three states, all pinned here:
///   reference exists            -> compare, and a mismatch is a breach
///   no reference, corpus empty  -> defer the capture to after the first round
///   no reference, corpus filled -> breach, and never silently capture
/// </summary>
public class SentinelLifecycleTests
{
    /// <summary>Serves a canned embedding, counting requests so capture timing is observable.</summary>
    private sealed class StubHandler(Func<string, float[]> vectorFor) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var body = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            var vector = vectorFor(body);
            var json = "{\"data\":[{\"embedding\":[" +
                       string.Join(",", vector.Select(v => v.ToString("R"))) +
                       "],\"index\":0}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>In-memory reference store, so no disk or merkle store is needed.</summary>
    private sealed class FakeStore(bool corpusEmpty) : IEmbeddingSentinelStore
    {
        public SentinelReference? Stored { get; private set; }
        public int SaveCount { get; private set; }
        public string? SavedText { get; private set; }
        public float[]? SavedVector { get; private set; }
        public bool Pending { get; private set; }

        public Task<bool> HasReferenceAsync(CancellationToken ct = default)
            => Task.FromResult(Stored is not null);

        public Task<SentinelReference?> LoadAsync(CancellationToken ct = default)
            => Task.FromResult(Stored);

        public Task SaveAsync(string sentinelText, float[] vector, CancellationToken ct = default)
        {
            SaveCount++;
            SavedText = sentinelText;
            SavedVector = vector;
            Stored = new SentinelReference(sentinelText, vector);
            Pending = false;   // mirrors the file store clearing its marker
            return Task.CompletedTask;
        }

        public Task<bool> IsCorpusEmptyAsync(CancellationToken ct = default)
            => Task.FromResult(corpusEmpty);

        public Task<bool> IsCapturePendingAsync(CancellationToken ct = default)
            => Task.FromResult(Pending);

        public Task MarkCapturePendingAsync(CancellationToken ct = default)
        {
            Pending = true;
            return Task.CompletedTask;
        }
    }

    private static float[] UnitVector(int dim, float scale = 1f, int seed = 0)
    {
        var v = new float[dim];
        var rnd = new Random(seed);
        float sum = 0;
        for (var i = 0; i < dim; i++) { v[i] = (float)(rnd.NextDouble() - 0.5); sum += v[i] * v[i]; }
        var norm = MathF.Sqrt(sum);
        for (var i = 0; i < dim; i++) v[i] = v[i] / norm * scale;
        return v;
    }

    private static (OpenAICompatibleEmbeddingService svc, StubHandler handler) Build(
        Func<string, float[]> vectorFor, EmbeddingValidationOptions options,
        IEmbeddingSentinelStore? store)
    {
        var handler = new StubHandler(vectorFor);
        var svc = new OpenAICompatibleEmbeddingService(
            new HttpClient(handler), "http://stub/v1", "stub-model", "key",
            NullLogger<OpenAICompatibleEmbeddingService>.Instance, options, store);
        return (svc, handler);
    }

    private static EmbeddingValidationOptions Options() => new()
    {
        ExpectedDimension = 8,
        ExpectedL2Norm = 1f,
        NormTolerance = 0.01f,
        ThrowOnShapeFailure = true,
        ThrowOnContinuityFailure = true,
        SentinelText = "the sentinel",
        MinSentinelCosine = 0.99f,
    };

    // -- State 1: reference exists --------------------------------------------

    [Fact]
    public async Task ExistingReference_IsCompared_AndPassesWhenUnchanged()
    {
        var store = new FakeStore(corpusEmpty: false);
        var vec = UnitVector(8, seed: 7);
        await store.SaveAsync("the sentinel", vec);

        var (svc, _) = Build(_ => vec, Options(), store);

        var result = await svc.GenerateEmbeddingsAsync(["hello"]);

        Assert.Single(result);
        Assert.Equal(0, store.SaveCount - 1);   // one save above, none added by the service
    }

    [Fact]
    public async Task ExistingReference_ForADifferentText_IsABreach()
    {
        // A reference captured for another string makes the cosine meaningless in BOTH
        // directions: it can neither pass meaningfully nor fail meaningfully.
        var store = new FakeStore(corpusEmpty: false);
        await store.SaveAsync("a completely different sentinel", UnitVector(8, seed: 3));

        var (svc, _) = Build(_ => UnitVector(8, seed: 3), Options(), store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GenerateEmbeddingsAsync(["hello"]));
        Assert.Contains("DIFFERENT sentinel text", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // -- State 2: no reference, corpus empty -> defer ------------------------

    [Fact]
    public async Task EmptyCorpus_WithNoReference_DefersCaptureAndDoesNotThrow()
    {
        var store = new FakeStore(corpusEmpty: true);
        var (svc, _) = Build(_ => UnitVector(8, seed: 1), Options(), store);

        // Must not throw: there is nothing to protect yet.
        var result = await svc.GenerateEmbeddingsAsync(["hello"]);

        Assert.Single(result);
        Assert.Equal(0, store.SaveCount);       // capture is deferred, not taken at check time
    }

    [Fact]
    public async Task DeferredCapture_IsTakenOnFirstRound_ThenNotAgain()
    {
        var store = new FakeStore(corpusEmpty: true);
        var (svc, handler) = Build(_ => UnitVector(8, seed: 1), Options(), store);

        // First embedding call arms the pending capture.
        await svc.GenerateEmbeddingsAsync(["hello"]);
        var afterCheck = handler.RequestCount;

        // Round completes -> capture happens.
        await svc.TryCaptureReferenceAsync();
        Assert.Equal(1, store.SaveCount);
        Assert.Equal("the sentinel", store.SavedText);
        Assert.Equal(8, store.SavedVector!.Length);
        Assert.True(handler.RequestCount > afterCheck, "capture must embed the sentinel");

        // Later rounds are no-ops.
        await svc.TryCaptureReferenceAsync();
        await svc.TryCaptureReferenceAsync();
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task Capture_IsNoOp_WhenNothingWasPending()
    {
        // Reference already present: the service never arms a capture, so the hook does nothing.
        var store = new FakeStore(corpusEmpty: false);
        await store.SaveAsync("the sentinel", UnitVector(8, seed: 5));

        var (svc, _) = Build(_ => UnitVector(8, seed: 5), Options(), store);
        await svc.GenerateEmbeddingsAsync(["hello"]);

        await svc.TryCaptureReferenceAsync();

        Assert.Equal(1, store.SaveCount);   // only the seeding save
    }

    // -- State 3: no reference, corpus NON-empty -> breach --------------------

    [Fact]
    public async Task FilledCorpus_WithNoReference_IsABreach_AndNeverSilentlyCaptures()
    {
        // THE case that matters. A corpus exists whose space is unknown. Capturing a reference
        // from the live server here would compare the space against itself and pass trivially -
        // the exact failure that had disabled the guard.
        var store = new FakeStore(corpusEmpty: false);
        var (svc, _) = Build(_ => UnitVector(8, seed: 2), Options(), store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GenerateEmbeddingsAsync(["hello"]));

        Assert.Contains("BREACH", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, store.SaveCount);   // must NOT have captured
    }

    [Fact]
    public async Task FilledCorpus_WithNoReference_LogsButDoesNotThrow_WhenThrowingDisabled()
    {
        var opts = Options();
        opts.ThrowOnContinuityFailure = false;
        var store = new FakeStore(corpusEmpty: false);
        var (svc, _) = Build(_ => UnitVector(8, seed: 2), opts, store);

        var result = await svc.GenerateEmbeddingsAsync(["hello"]);

        Assert.Single(result);
        Assert.Equal(0, store.SaveCount);   // still refuses to capture
    }

    // -- Degradation ----------------------------------------------------------

    [Fact]
    public async Task NoStoreSupplied_DegradesToShapeOnly_WithoutThrowing()
    {
        var (svc, _) = Build(_ => UnitVector(8, seed: 1), Options(), store: null);

        var result = await svc.GenerateEmbeddingsAsync(["hello"]);

        Assert.Single(result);
    }

    // -- Interrupted bootstrap ------------------------------------------------

    [Fact]
    public async Task InterruptedBootstrap_ResumesInsteadOfReportingABreach()
    {
        // The process died mid-round: the corpus now has nodes but no reference, and the pending
        // marker survived. That is an unfinished bootstrap, NOT a breach - the corpus was built
        // by this server and is internally consistent. Reporting a breach here would be a false
        // alarm that trains the reader to ignore the real one.
        var store = new FakeStore(corpusEmpty: false);
        await store.MarkCapturePendingAsync();   // left behind by the crashed run

        var (svc, _) = Build(_ => UnitVector(8, seed: 1), Options(), store);

        // Must not throw.
        var result = await svc.GenerateEmbeddingsAsync(["hello"]);
        Assert.Single(result);

        // And the capture must still be honoured on the next completed round.
        await svc.TryCaptureReferenceAsync();
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task EmptyCorpus_WritesThePendingMarker_SoARestartCanResume()
    {
        var store = new FakeStore(corpusEmpty: true);
        var (svc, _) = Build(_ => UnitVector(8, seed: 1), Options(), store);

        await svc.GenerateEmbeddingsAsync(["hello"]);

        Assert.True(store.Pending, "the deferral must be persisted, not only held in memory");
    }

    [Fact]
    public async Task SuccessfulCapture_ClearsThePendingMarker()
    {
        var store = new FakeStore(corpusEmpty: true);
        var (svc, _) = Build(_ => UnitVector(8, seed: 1), Options(), store);

        await svc.GenerateEmbeddingsAsync(["hello"]);
        Assert.True(store.Pending);

        await svc.TryCaptureReferenceAsync();

        Assert.False(store.Pending);
        Assert.NotNull(store.Stored);
    }
}
