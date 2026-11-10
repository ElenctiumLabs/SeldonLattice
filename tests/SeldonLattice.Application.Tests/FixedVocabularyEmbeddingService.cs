using Microsoft.Extensions.AI;
using SeldonLattice.Application.Memory;

namespace SeldonLattice.Application.Tests;

/// <summary>
/// Deterministic, embedding-call-free stand-in for <see cref="ITextEmbeddingGenerationService"/>.
/// Emits the unit-normalized sum of a fixed per-token vocabulary (defaulting to a stable
/// 1-hot hash), so a test can express "semantic" nearness by registering synonym entries
/// (e.g. "guaranteed" ~ "basic", "annual" ~ "income").
/// </summary>
public sealed class FixedVocabularyEmbeddingService(IReadOnlyDictionary<string, float[]>? vocabulary = null)
    : IEmbeddingGenerator<string, Embedding<float>>
{
    public const int Dimensions = 8;

    private readonly IReadOnlyDictionary<string, float[]> _vocabulary = vocabulary ?? new Dictionary<string, float[]>();

    public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>([.. values.Select(Embed)]));
    }

    private Embedding<float> Embed(string text)
    {
        var vec = new float[Dimensions];
        foreach (var token in ClaimMemoryRetriever.Tokenize(text))
        {
            if (_vocabulary.TryGetValue(token, out var known))
            {
                for (var i = 0; i < Dimensions; i++)
                    vec[i] += known[i];
            }
            else
            {
                var index = StableIndex(token);
                vec[index] += 1f;
            }
        }

        var norm = Math.Sqrt(vec.Sum(x => x * x));
        if (norm > 0)
        {
            for (var i = 0; i < vec.Length; i++)
                vec[i] = (float)(vec[i] / norm);
        }

        return new Embedding<float>(vec);
    }

    private static int StableIndex(string token)
    {
        var hash = token.GetStableHashCode();
        return (hash & int.MaxValue) % Dimensions;
    }

    public void Dispose()
    {
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is not null)
            return null;

        if (serviceType == typeof(EmbeddingGeneratorMetadata))
            return new EmbeddingGeneratorMetadata(nameof(FixedVocabularyEmbeddingService));

        return serviceType.IsInstanceOfType(this) ? this : null;
    }
}

internal static class HashExtensions
{
    // FNV-1a, stable across runs (string.GetHashCode is randomized per process).
    public static int GetStableHashCode(this string value)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in value)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash;
        }
    }
}