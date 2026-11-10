using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using SeldonLattice.Application.Memory;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Domain.Options;
using SeldonLattice.Infrastructure.Storage;
using Xunit;

namespace SeldonLattice.Application.Tests;

public class ClaimMemoryRetrieverTests : IDisposable
{
    private readonly string _tempDir;

    public ClaimMemoryRetrieverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"seldon_claim_memory_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private static Kernel CreateTestKernel() => Kernel.CreateBuilder().Build();

    private static async Task<FasterMerkleStore> SeedClaimsAsync(string dir, params ArgumentClaimNode[] claims)
    {
        var store = new FasterMerkleStore(dir);
        foreach (var c in claims)
            await store.PutBlobAsync(c);
        return store;
    }

    [Fact]
    public void Jaccard_IsZeroOnDisjointTokenSets()
    {
        Assert.Equal(0f, ClaimMemoryRetriever.Jaccard(
            ClaimMemoryRetriever.Tokenize("basic income"),
            ClaimMemoryRetriever.Tokenize("quantum gravity")));
    }

    [Fact]
    public void Jaccard_IsOneOnIdenticalTokenSets()
    {
        Assert.Equal(1f, ClaimMemoryRetriever.Jaccard(
            ClaimMemoryRetriever.Tokenize("basic income floor"),
            ClaimMemoryRetriever.Tokenize("income basic floor")));
    }

    [Fact]
    public void Tokenize_LowercasesDropsShortWordsAndDeduplicates()
    {
        var tokens = ClaimMemoryRetriever.Tokenize("A Tiered Basic Income: tiered basic income!");
        Assert.Contains("tiered", tokens);
        Assert.Contains("basic", tokens);
        Assert.Contains("income", tokens);
        Assert.DoesNotContain("a", tokens);
        Assert.Equal(tokens.Distinct().Count(), tokens.Length);
    }

    [Fact]
    public async Task Retrieve_ReturnsMatchingClaimsRankedBySimilarity()
    {
        var close = new ArgumentClaimNode
        {
            SessionId = "s1", Level = 1,
            ClaimText = "A tiered basic income requires a new fiscal instrument",
            Confidence = 0.8f,
            FalsifiedBy = "#data rejects", SupportedBy = ["cid-1"], ParentHashes = ["cid-1"],
        };
        var distant = new ArgumentClaimNode
        {
            SessionId = "s1", Level = 2,
            ClaimText = "Freedom depends only on the love of another self",
            Confidence = 0.9f,
            SupportedBy = ["cid-2"], ParentHashes = ["cid-2"],
        };
        var store = await SeedClaimsAsync(_tempDir, close, distant);

        var retriever = new ClaimMemoryRetriever(store, CreateTestKernel());
        var recalled = await retriever.RetrieveAsync("basic income", "current-session");

        var single = Assert.Single(recalled);
        Assert.Equal("s1", single.SessionId);
        Assert.Contains("fiscal instrument", single.Claim);
        Assert.Equal(0.8f, single.Confidence);
        Assert.True(single.Similarity > 0);
    }

    [Fact]
    public async Task Retrieve_ExcludesClaimsFromTheExcludedSession()
    {
        var store = await SeedClaimsAsync(
            _tempDir,
            new ArgumentClaimNode
            {
                SessionId = "me", Level = 1,
                ClaimText = "basic income boundary to test exclusion",
                Confidence = 0.9f, SupportedBy = ["c"], ParentHashes = ["c"],
            },
            new ArgumentClaimNode
            {
                SessionId = "other", Level = 1,
                ClaimText = "basic income was already adjudicated here",
                Confidence = 0.7f, SupportedBy = ["c"], ParentHashes = ["c"],
            });

        var retriever = new ClaimMemoryRetriever(store, CreateTestKernel());
        var recalled = await retriever.RetrieveAsync("basic income", "me");

        var single = Assert.Single(recalled);
        Assert.Equal("other", single.SessionId);
    }

    [Fact]
    public async Task Retrieve_RespectsMaxResultsAndMinSimilarity()
    {
        var store = await SeedClaimsAsync(
            _tempDir,
            new ArgumentClaimNode { SessionId = "a", Level = 1, ClaimText = "basic income monetization registry", Confidence = 0.6f, SupportedBy = [], ParentHashes = [] },
            new ArgumentClaimNode { SessionId = "b", Level = 1, ClaimText = "basic income payment architecture", Confidence = 0.9f, SupportedBy = [], ParentHashes = [] },
            new ArgumentClaimNode { SessionId = "c", Level = 1, ClaimText = "quantum gravity collapse timing", Confidence = 0.99f, SupportedBy = [], ParentHashes = [] });

        var retriever = new ClaimMemoryRetriever(store, CreateTestKernel());
        var recalled = await retriever.RetrieveAsync("basic income", "x", new ClaimMemoryQueryOptions { MaxResults = 1 });
        Assert.Single(recalled);

        recalled = await retriever.RetrieveAsync("basic income", "x", new ClaimMemoryQueryOptions { MinSimilarity = 0.9f });
        Assert.Empty(recalled);
    }

    [Fact]
    public async Task Retrieve_EmptyQueryOrEmptyStore_ReturnsEmpty()
    {
        var store = await SeedClaimsAsync(_tempDir);
        var retriever = new ClaimMemoryRetriever(store, CreateTestKernel());

        Assert.Empty(await retriever.RetrieveAsync("   ", "x"));
        Assert.Empty(await retriever.RetrieveAsync("basic income", "x"));
    }

    [Fact]
    public void Cosine_IdenticalVectors_IsOne()
    {
        Assert.Equal(1f, ClaimMemoryRetriever.Cosine([1f, 2f, 3f], [1f, 2f, 3f]));
    }

    [Fact]
    public void Cosine_OrthogonalVectors_IsZero()
    {
        Assert.Equal(0f, ClaimMemoryRetriever.Cosine([1f, 0f], [0f, 1f]));
    }

    [Fact]
    public void Cosine_ZeroOrMismatchedLength_IsZero()
    {
        Assert.Equal(0f, ClaimMemoryRetriever.Cosine([], [1f, 2f]));
        Assert.Equal(0f, ClaimMemoryRetriever.Cosine([0f, 0f], [0f, 0f]));
        Assert.Equal(0f, ClaimMemoryRetriever.Cosine([1f, 2f], [1f, 2f, 3f]));
    }

    [Fact]
    public async Task Retrieve_EmbeddingMode_RecallsSemanticallyEquivalentClaimThatTokenOverlapMisses()
    {
        // Synonym vocabulary: "guaranteed" ~ "basic", "annual" ~ "income".
        var vocab = new Dictionary<string, float[]>
        {
            ["basic"]      = [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f],
            ["guaranteed"] = [0.8f, 0.2f, 0f, 0f, 0f, 0f, 0f, 0f],
            ["income"]     = [0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f],
            ["annual"]     = [0.2f, 0.8f, 0f, 0f, 0f, 0f, 0f, 0f],
        };
        var embeddingKernelBuilder = Kernel.CreateBuilder();
        embeddingKernelBuilder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_ => new FixedVocabularyEmbeddingService(vocab));
        var embeddingKernel = embeddingKernelBuilder.Build();

        // "guaranteed annual income" shares only "income" with the query, so its Jaccard
        // score (0.25) sits below MinSimilarity 0.3 in TokenOverlap mode...
        var store = await SeedClaimsAsync(
            _tempDir,
            new ArgumentClaimNode
            {
                SessionId = "prior", Level = 3,
                ClaimText = "guaranteed annual income countermeasures",
                Confidence = 0.7f,
                SupportedBy = ["c"], ParentHashes = ["c"],
            });

        var tokenRetriever = new ClaimMemoryRetriever(store, CreateTestKernel());
        Assert.Empty(await tokenRetriever.RetrieveAsync(
            "basic income", "current",
            new ClaimMemoryQueryOptions { MinSimilarity = 0.3f }));

        // ...but its embedding cosine with "basic income" is high, so Embedding mode
        // recalls it and reports the cosine as the similarity.
        var embeddingRetriever = new ClaimMemoryRetriever(store, embeddingKernel);
        var recalled = await embeddingRetriever.RetrieveAsync(
            "basic income", "current",
            new ClaimMemoryQueryOptions { MinSimilarity = 0.3f, Mode = ClaimMemoryMode.Embedding });

        var single = Assert.Single(recalled);
        Assert.Equal("prior", single.SessionId);
        Assert.Equal(3, single.Level);
        Assert.Equal(0.7f, single.Confidence);
        Assert.True(single.Similarity > 0.5f, $"expected high cosine similarity, got {single.Similarity}");
        Assert.NotEqual(JaccardFor(single.Claim), single.Similarity);
    }

    [Fact]
    public async Task Retrieve_EmbeddingMode_RespectsMinSimilarityOnCosineScores()
    {
        var vocab = new Dictionary<string, float[]>
        {
            ["basic"]  = [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f],
            ["income"] = [0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f],
        };
        var embeddingKernelBuilder = Kernel.CreateBuilder();
        embeddingKernelBuilder.Services.AddSingleton<
            IEmbeddingGenerator<string, Embedding<float>>>(
            _ => new FixedVocabularyEmbeddingService(vocab));
        var embeddingKernel = embeddingKernelBuilder.Build();

        var store = await SeedClaimsAsync(
            _tempDir,
            new ArgumentClaimNode
            {
                SessionId = "prior", Level = 1,
                ClaimText = "basic income payment architecture",
                Confidence = 0.6f,
                SupportedBy = ["c"], ParentHashes = ["c"],
            });

        var retriever = new ClaimMemoryRetriever(store, embeddingKernel);

        Assert.NotEmpty(await retriever.RetrieveAsync("basic income", "x",
            new ClaimMemoryQueryOptions { MinSimilarity = 0.1f, Mode = ClaimMemoryMode.Embedding }));
        Assert.Empty(await retriever.RetrieveAsync("basic income", "x",
            new ClaimMemoryQueryOptions { MinSimilarity = 0.99f, Mode = ClaimMemoryMode.Embedding }));
    }

    private static float JaccardFor(string text) =>
        ClaimMemoryRetriever.Jaccard(
            ClaimMemoryRetriever.Tokenize("basic income"),
            ClaimMemoryRetriever.Tokenize(text));
}