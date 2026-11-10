using Microsoft.Extensions.AI;

namespace SeldonLattice.Application.Tests.Stubs;

/// <summary>
/// Stub embedding generator for testing. Returns deterministic embeddings
/// based on string content so that similar strings have high cosine similarity.
/// </summary>
public sealed class StubEmbeddingService
    : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var results = new List<Embedding<float>>();

            foreach (var text in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(new Embedding<float>(CreateVector(text)));
            }

            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(results));
        }
        catch (Exception exception)
        {
            return Task.FromException<GeneratedEmbeddings<Embedding<float>>>(exception);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is not null)
            return null;

        if (serviceType == typeof(EmbeddingGeneratorMetadata))
            return new EmbeddingGeneratorMetadata(nameof(StubEmbeddingService));

        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
    }

    private static float[] CreateVector(string text)
    {
        // Deterministic bag-of-words: each word maps to a fixed dimension
        // based on its first letter (26 dimensions). Sentences sharing words
        // tend to have higher cosine similarity.
        var vector = new float[26];

        var words = text.ToLowerInvariant().Split(
            [' ', '.', ',', '!', '?', ';', ':', '-', '_', '(', ')',
             '[', ']', '{', '}', '"', '\''],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            if (word.Length > 0 && word[0] is >= 'a' and <= 'z')
                vector[word[0] - 'a']++;
        }

        return vector;
    }
}
