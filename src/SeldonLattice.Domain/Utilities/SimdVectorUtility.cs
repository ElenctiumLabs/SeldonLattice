using System.Numerics;

namespace SeldonLattice.Domain.Utilities;

/// <summary>
/// High-performance SIMD-accelerated vector operations for embedding similarity calculations.
/// Uses hardware vectorization when available for fast cosine similarity computation.
/// </summary>
public static class SimdVectorUtility
{
    /// <summary>
    /// Computes the cosine similarity between two vectors using SIMD optimization.
    /// Cosine similarity = (A·B) / (||A|| × ||B||), ranging from -1 to 1.
    /// For embeddings, typically returns values from 0 to 1 where 1 means identical direction.
    /// </summary>
    /// <param name="a">First vector (e.g., embedding from the proposer's response).</param>
    /// <param name="b">Second vector (e.g., embedding from the skeptic's response).</param>
    /// <returns>Cosine similarity score between -1 and 1 (0 if either vector is zero).</returns>
    /// <exception cref="ArgumentException">Thrown when vectors have different lengths.</exception>
    public static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) throw new ArgumentException("Vectors must be same length");

        var len = a.Length;
        var i = 0;
        float dot = 0, normA = 0, normB = 0;
        var vecSize = Vector<float>.Count;

        // SIMD-optimized loop for hardware-accelerated computation
        if (vecSize > 0 && len >= vecSize)
        {
            var vdot = Vector<float>.Zero;
            var vnormA = Vector<float>.Zero;
            var vnormB = Vector<float>.Zero;
            for (; i <= len - vecSize; i += vecSize)
            {
                var va = new Vector<float>(a, i);
                var vb = new Vector<float>(b, i);
                vdot += va * vb;
                vnormA += va * va;
                vnormB += vb * vb;
            }
            dot = Vector.Dot(vdot, Vector<float>.One);
            normA = Vector.Dot(vnormA, Vector<float>.One);
            normB = Vector.Dot(vnormB, Vector<float>.One);
        }

        // Scalar fallback for remaining elements
        for (; i < len; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        // Avoid division by zero for null/empty vectors
        if (normA == 0f || normB == 0f) return 0f;
        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }

    /// <summary>
    /// Computes the element-wise average of two embedding vectors.
    /// Used to find the middle ground between abstract and practical perspectives.
    /// </summary>
    /// <param name="a">First embedding vector.</param>
    /// <param name="b">Second embedding vector.</param>
    /// <returns>A new vector where each element is (a[i] + b[i]) / 2.</returns>
    /// <exception cref="ArgumentException">Thrown when vectors have different lengths.</exception>
    public static float[] AverageEmbeddings(float[] a, float[] b)
    {
        if (a.Length != b.Length) throw new ArgumentException("Vectors must be same length");
        
        var avg = new float[a.Length];
        for (var i = 0; i < a.Length; i++)
            avg[i] = (a[i] + b[i]) / 2f;
        return avg;
    }
}
