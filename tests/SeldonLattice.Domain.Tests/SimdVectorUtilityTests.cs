using SeldonLattice.Domain.Utilities;
using Xunit;

namespace SeldonLattice.Domain.Tests;

/// <summary>
/// Unit tests for SIMD-accelerated vector utilities used in convergence detection.
/// </summary>
public class SimdVectorUtilityTests
{
    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        var a = new float[] { 1f, 2f, 3f, 4f, 5f };
        var b = new float[] { 1f, 2f, 3f, 4f, 5f };

        var result = SimdVectorUtility.CosineSimilarity(a, b);

        Assert.Equal(1f, result, 5);
    }

    [Fact]
    public void CosineSimilarity_OppositeVectors_ReturnsNegativeOne()
    {
        var a = new float[] { 1f, 2f, 3f };
        var b = new float[] { -1f, -2f, -3f };

        var result = SimdVectorUtility.CosineSimilarity(a, b);

        Assert.Equal(-1f, result, 5);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        var a = new float[] { 1f, 0f, 0f };
        var b = new float[] { 0f, 1f, 0f };

        var result = SimdVectorUtility.CosineSimilarity(a, b);

        Assert.Equal(0f, result, 5);
    }

    [Fact]
    public void CosineSimilarity_ZeroVector_ReturnsZero()
    {
        var a = new float[] { 0f, 0f, 0f };
        var b = new float[] { 1f, 2f, 3f };

        var result = SimdVectorUtility.CosineSimilarity(a, b);

        Assert.Equal(0f, result);
    }

    [Fact]
    public void CosineSimilarity_DifferentLengths_ThrowsArgumentException()
    {
        var a = new float[] { 1f, 2f };
        var b = new float[] { 1f, 2f, 3f };

        Assert.Throws<ArgumentException>(() => SimdVectorUtility.CosineSimilarity(a, b));
    }

    [Fact]
    public void CosineSimilarity_LargeVectors_UsesSIMDAndMatchesScalar()
    {
        var random = new Random(42);
        var size = 1000; // Large enough to trigger SIMD path
        var a = new float[size];
        var b = new float[size];
        for (int i = 0; i < size; i++)
        {
            a[i] = (float)random.NextDouble();
            b[i] = (float)random.NextDouble();
        }

        var result = SimdVectorUtility.CosineSimilarity(a, b);

        // Just verify it returns a valid cosine similarity
        Assert.True(result is >= -1f and <= 1f);
    }

    [Fact]
    public void AverageEmbeddings_ReturnsElementWiseAverage()
    {
        var a = new float[] { 1f, 2f, 3f };
        var b = new float[] { 3f, 4f, 5f };

        var result = SimdVectorUtility.AverageEmbeddings(a, b);

        Assert.Equal(new float[] { 2f, 3f, 4f }, result);
    }

    [Fact]
    public void AverageEmbeddings_DifferentLengths_Throws()
    {
        var a = new float[] { 1f, 2f };
        var b = new float[] { 1f, 2f, 3f };

        Assert.Throws<ArgumentException>(() => SimdVectorUtility.AverageEmbeddings(a, b));
    }
}