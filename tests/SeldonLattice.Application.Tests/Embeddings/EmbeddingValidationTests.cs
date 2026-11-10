using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SeldonLattice.Infrastructure.Services;
using Xunit;

// The service carries [Experimental("SKEXP0001")] because it implements a Semantic Kernel
// experimental interface. Test code exercises it deliberately.
#pragma warning disable SKEXP0001

namespace SeldonLattice.Application.Tests.Embeddings;

/// <summary>
/// Tests for the embedding validation added during the llama.cpp migration.
///
/// The motivating finding: after repointing at llama.cpp, embedding the EXACT text of a
/// stored node produced cosine ~0.67 against its stored vector - while dimension (768) and
/// L2 norm (1.0) were both perfect. A shape check alone would have passed a vector space
/// that had silently changed, and every cosine metric would have read the difference as
/// spurious advance. These tests pin the check that catches it.
/// </summary>
public class EmbeddingValidationTests
{
    /// <summary>Serves a canned OpenAI-shaped embedding response.</summary>
    private sealed class StubHandler(Func<string, float[]> vectorFor) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content!.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            var vector = vectorFor(body);
            var json = "{\"model\":\"stub\",\"data\":[{\"embedding\":[" +
                       string.Join(",", vector.Select(v => v.ToString("R"))) +
                       "],\"index\":0,\"object\":\"embedding\"}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private static OpenAICompatibleEmbeddingService Build(
        Func<string, float[]> vectorFor, EmbeddingValidationOptions options)
        => new(new HttpClient(new StubHandler(vectorFor)),
               "http://stub/v1", "stub-model", "key",
               NullLogger<OpenAICompatibleEmbeddingService>.Instance, options);

    /// <summary>Unit vector of the given dimension, optionally scaled.</summary>
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

    [Fact]
    public async Task ShapeCheck_Passes_ForCorrectDimensionAndNorm()
    {
        var opts = new EmbeddingValidationOptions
        {
            ExpectedDimension = 8, ExpectedL2Norm = 1f,
            ThrowOnShapeFailure = true,
            SentinelReference = null,           // continuity disabled for this test
        };
        var svc = Build(_ => UnitVector(8), opts);

        var result = await svc.GenerateEmbeddingsAsync(["hello"]);

        Assert.Single(result);
        Assert.Equal(8, result[0].Length);
    }

    [Fact]
    public async Task ShapeCheck_Throws_OnWrongDimension()
    {
        var opts = new EmbeddingValidationOptions
        {
            ExpectedDimension = 768,            // server returns 8
            ThrowOnShapeFailure = true,
        };
        var svc = Build(_ => UnitVector(8), opts);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GenerateEmbeddingsAsync(["hello"]));
        Assert.Contains("dimension mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShapeCheck_Throws_OnUnnormalisedVector()
    {
        // Right dimension, wrong scale: exactly the "wrong shape, right numbers" case that a
        // cosine-only pipeline would otherwise swallow.
        var opts = new EmbeddingValidationOptions
        {
            ExpectedDimension = 8, ExpectedL2Norm = 1f, NormTolerance = 0.01f,
            ThrowOnShapeFailure = true,
        };
        var svc = Build(_ => UnitVector(8, scale: 5f), opts);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GenerateEmbeddingsAsync(["hello"]));
        Assert.Contains("L2-normalised", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ContinuityCheck_Passes_WhenSpaceIsUnchanged()
    {
        var reference = UnitVector(8, seed: 42);
        var opts = new EmbeddingValidationOptions
        {
            ExpectedDimension = 8,
            SentinelReference = reference,
            MinSentinelCosine = 0.99f,
            ThrowOnContinuityFailure = true,
        };
        var svc = Build(_ => reference, opts);   // server returns the reference itself

        // First call runs the continuity check; must not throw.
        var result = await svc.GenerateEmbeddingsAsync(["sentinel"]);

        Assert.Single(result);
    }

    [Fact]
    public async Task ContinuityCheck_Throws_WhenVectorSpaceChanged()
    {
        // THE motivating case. A different space with IDENTICAL shape: 8 dims, unit norm.
        // The shape check cannot see this; the continuity check must.
        var reference = UnitVector(8, seed: 42);
        var differentSpace = UnitVector(8, seed: 99);
        var cosine = Cosine(reference, differentSpace);
        Assert.True(cosine < 0.99, $"test vectors must differ (got {cosine:F4})");

        var opts = new EmbeddingValidationOptions
        {
            ExpectedDimension = 8,
            ExpectedL2Norm = 1f,
            ThrowOnShapeFailure = true,          // shape is perfect...
            SentinelReference = reference,
            MinSentinelCosine = 0.99f,
            ThrowOnContinuityFailure = true,     // ...so continuity must be the one that fires
        };
        var svc = Build(_ => differentSpace, opts);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.GenerateEmbeddingsAsync(["sentinel"]));
        Assert.Contains("continuity check FAILED", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DIFFERENT SPACE", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ContinuityCheck_IsSkipped_WhenNoReferenceConfigured()
    {
        var opts = new EmbeddingValidationOptions
        {
            ExpectedDimension = 8,
            SentinelReference = null,
            SentinelReferencePath = null,
            ThrowOnContinuityFailure = true,
        };
        var svc = Build(_ => UnitVector(8), opts);

        // No reference: must degrade to "skipped", not throw and not silently pass a check
        // it never ran.
        var result = await svc.GenerateEmbeddingsAsync(["sentinel"]);

        Assert.Single(result);
    }

    [Fact]
    public async Task Validation_CanBeDisabled()
    {
        var opts = new EmbeddingValidationOptions { Enabled = false };
        var svc = Build(_ => UnitVector(3), opts);   // wrong dim, unnormalised: ignored

        var result = await svc.GenerateEmbeddingsAsync(["hello"]);

        Assert.Equal(3, result[0].Length);
    }

    private static float Cosine(float[] a, float[] b)
    {
        float dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return dot / (MathF.Sqrt(na) * MathF.Sqrt(nb));
    }
}
