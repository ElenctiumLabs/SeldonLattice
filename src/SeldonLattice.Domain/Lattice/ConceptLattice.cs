using SeldonLattice.Domain.Utilities;

namespace SeldonLattice.Domain.Lattice;

/// <summary>
/// S1 substrate of the Reasoning Integrity Layer: a minimal concept lattice over
/// <c>DialecticalNode</c>. A formal concept is (intent = embedding vector, extent = set of
/// support CIDs). Thesis and antithesis are concepts over (module vocabulary × evidence
/// extents); the synthesis is a least-upper-bound join under that vocabulary; a "sustained
/// theme" is a detected fixed point of the join (the join already dominates a pole, so the
/// lattice - and the ascent - cannot move higher in that vocabulary).
///
/// The intent lattice is the componentwise-ordered <c>R^d</c> lattice (join = lub = componentwise
/// max, meet = glb = componentwise min); extents join by union and meet by intersection.
/// </summary>
public static class ConceptLattice
{
    /// <summary>Least-upper-bound join of intent vectors (componentwise max).</summary>
    public static float[] Lub(params float[][] intents) => intents switch
    {
        [] => [],
        [var only] => only,
        _ => vectorwise(intents, MathF.Max),
    };

    /// <summary>Greatest-lower-bound meet of intent vectors (componentwise min).</summary>
    public static float[] Glb(params float[][] intents) => intents switch
    {
        [] => [],
        [var only] => only,
        _ => vectorwise(intents, MathF.Min),
    };

    /// <summary>Join the extents of two concepts (union, sorted, deduplicated).</summary>
    public static IReadOnlyList<string> JoinExtents(
        IEnumerable<string> left, IEnumerable<string> right) =>
        [.. (left ?? []).Concat(right ?? []).Distinct().OrderBy(s => s, StringComparer.Ordinal)];

    /// <summary>Meet the extents of two concepts (intersection, sorted).</summary>
    public static IReadOnlyList<string> MeetExtents(
        IEnumerable<string> left, IEnumerable<string> right) =>
        [.. (left ?? []).Intersect(right ?? []).OrderBy(s => s, StringComparer.Ordinal)];

    /// <summary>Midpoint vector of the two pole intents (the synthetic mean).</summary>
    public static float[] Mid(float[] thesis, float[] antithesis) =>
        SimdVectorUtility.AverageEmbeddings(thesis, antithesis);

    /// <summary>
    /// Deviation of the join from the pole midpoint:
    /// <c>||join − mid(thesis, antithesis)||</c> (Euclidean). A join parked at the midpoint
    /// (near-zero deviation) is well-mediating; a join far from it tilts toward one pole or
    /// the other.
    /// </summary>
    public static float MediationDeviation(float[] join, float[] thesis, float[] antithesis)
    {
        var mid = Mid(thesis, antithesis);
        var squared = 0f;
        var n = Math.Min(join.Length, mid.Length);
        for (var i = 0; i < n; i++)
        {
            var diff = join[i] - mid[i];
            squared += diff * diff;
        }
        return MathF.Sqrt(squared);
    }

    /// <summary>
    /// Novelty of the join vs the poles: <c>cos(join, thesis) + cos(join, antithesis)</c>, the
    /// same sign convention as the S3 ascent objective
    /// <c>Σ [JointNovelty − λ·redundancy + μ·mediationDeviation]</c>. The term is small when the
    /// join collapses onto a pole and maximised (for unit vectors) when it splits the poles.
    /// </summary>
    public static float JoinNovelty(float[] join, float[] thesis, float[] antithesis) =>
        SimdVectorUtility.CosineSimilarity(join, thesis) +
        SimdVectorUtility.CosineSimilarity(join, antithesis);

    /// <summary>
    /// Fixed-point / "sustained theme" detection in the lattice sense: <c>lub(join, thesis) == join</c>
    /// - the join intent already dominates the thesis componentwise, so re-joining with the pole
    /// (or with the inverse pole) no longer moves the lattice.
    ///
    /// Reading (updated 2026-10-11, negative dialectics): this is NOT simply "ascent failed".
    /// It is the lattice-theoretic statement that <b>the current vocabulary cannot absorb this
    /// contradiction</b> - the join already dominates a pole, so no further movement is
    /// available in this vocabulary. Under the negative-dialectic reading that is a FINDING
    /// to be recorded, not a failure to be retried: the vocabulary, not the reasoning, is the
    /// limiting factor. Pair it with <c>AscentQualityScorer.IrreducibleTension</c>, which
    /// measures how much live tension survives the synthesis.
    /// </summary>
    public static bool IsSustainedTheme(float[] join, float[] thesis, float epsilon = 1e-5f)
    {
        if (join.Length == 0 || thesis.Length == 0)
            return false;
        var n = Math.Min(join.Length, thesis.Length);
        for (var i = 0; i < n; i++)
        {
            if (thesis[i] > join[i] + epsilon)
                return false;
        }
        // Uncovered dimensions count as missing dominance.
        if (thesis.Length > join.Length)
            return !thesis.Skip(join.Length).Any(v => v > epsilon);
        return true;
    }

    private static float[] vectorwise(float[][] intents, Func<float, float, float> op)
    {
        var width = intents.Max(v => v.Length);
        var result = new float[width];
        for (var i = 0; i < width; i++)
        {
            var acc = intents[0].Length > i ? intents[0][i] : 0f;
            for (var j = 1; j < intents.Length; j++)
            {
                var v = intents[j].Length > i ? intents[j][i] : 0f;
                acc = op(acc, v);
            }
            result[i] = acc;
        }
        return result;
    }
}