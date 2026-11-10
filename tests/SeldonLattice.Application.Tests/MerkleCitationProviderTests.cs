using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Infrastructure.Services;
using SeldonLattice.Infrastructure.Storage;
using Xunit;

namespace SeldonLattice.Application.Tests;

public class MerkleCitationProviderTests
{
    private class FixedEmbeddingService(float[] vector) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var results = values.Select(_ => new Embedding<float>(vector)).ToList();
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(results));
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceKey is not null) return null;
            if (serviceType == typeof(EmbeddingGeneratorMetadata))
                return new EmbeddingGeneratorMetadata(nameof(FixedEmbeddingService));
            return serviceType.IsInstanceOfType(this) ? this : null;
        }

        public void Dispose() { }
    }

    [Fact]
    [Obsolete("Obsolete")]
    public async Task GetTopCitationsAsync_ReturnsMostSimilarDocument()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "seldon-citation-test", Guid.NewGuid().ToString());
        var store = new FasterMerkleStore(baseDir, 10);

        // Create two documents with simple embeddings
        var doc1 = new IngestedDocument
        {
            ExternalId = "doc1",
            SourceId = "testsrc",
            Title = "Apple doc",
            Body = "About apples",
            Culture = "western",
            Domain = "food",
            SourceTimestamp = DateTime.UtcNow,
            IngestedAt = DateTime.UtcNow,
            Embedding = [1f, 0f, 0f, 0f],
            Metadata = new Dictionary<string, string> { ["url"] = "https://example.com/apple" }
        };

        var doc2 = new IngestedDocument
        {
            ExternalId = "doc2",
            SourceId = "testsrc",
            Title = "Banana doc",
            Body = "About bananas",
            Culture = "western",
            Domain = "food",
            SourceTimestamp = DateTime.UtcNow,
            IngestedAt = DateTime.UtcNow,
            Embedding = [0f, 1f, 0f, 0f],
            Metadata = new Dictionary<string, string> { ["url"] = "https://example.com/banana" }
        };

        await store.PutBlobAsync(doc1);
        await store.PutBlobAsync(doc2);

        // Query embedding matches doc1
        var embedding = new float[] { 1f, 0f, 0f, 0f };
        var embedService = new FixedEmbeddingService(embedding);

        // Build a Kernel that contains the embedding service so the MerkleCitationProvider can resolve it
        var kernelBuilder = Kernel.CreateBuilder();
        kernelBuilder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embedService);
        var kernel = kernelBuilder.Build();

        var provider = new MerkleCitationProvider(store, kernel, NullLogger<MerkleCitationProvider>.Instance);

        var results = await provider.GetTopCitationsAsync("apple query", topN: 2);

        Assert.NotNull(results);
        Assert.True(results.Count >= 1);
        Assert.Equal("doc1", results[0].Id);
        Assert.Equal("https://example.com/apple", results[0].Url);

        // Cleanup
        try { Directory.Delete(baseDir, true); } catch { }
    }
}