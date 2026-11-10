using SeldonLattice.Domain.Lattice;
using Xunit;

namespace SeldonLattice.Domain.Tests;

/// <summary>
/// Unit tests for the S1 concept-lattice substrate (Reasoning Integrity Layer):
/// join/meet over intent vectors and extents, the mediation-deviation and join-novelty
/// measures, and fixed-point ("sustained theme") detection.
/// </summary>
public class ConceptLatticeTests
{
    [Fact]
    public void Lub_ComponentwiseMax()
    {
        var result = ConceptLattice.Lub([1f, 5f, 2f], [4f, 3f, 6f]);
        Assert.Equal(new float[] { 4f, 5f, 6f }, result);
    }

    [Fact]
    public void Lub_SingleVector_ReturnsIt()
    {
        var v = new float[] { 2f, 3f };
        Assert.Equal(v, ConceptLattice.Lub(v));
    }

    [Fact]
    public void Glb_ComponentwiseMin()
    {
        var result = ConceptLattice.Glb([1f, 5f, 2f], [4f, 3f, 6f]);
        Assert.Equal(new float[] { 1f, 3f, 2f }, result);
    }

    [Fact]
    public void JoinExtents_UnionSortedDeduplicated()
    {
        var result = ConceptLattice.JoinExtents(["b", "a"], ["c", "a"]);
        Assert.Equal(["a", "b", "c"], result);
    }

    [Fact]
    public void MeetExtents_IntersectionSorted()
    {
        var result = ConceptLattice.MeetExtents(["b", "a", "c"], ["c", "a", "d"]);
        Assert.Equal(["a", "c"], result);
    }

    [Fact]
    public void Mid_ElementwiseAverage()
    {
        var result = ConceptLattice.Mid([0f, 2f], [2f, 4f]);
        Assert.Equal(new float[] { 1f, 3f }, result);
    }

    [Fact]
    public void MediationDeviation_ZeroWhenJoinIsTheMidpoint()
    {
        // join = mid(thesis, antithesis) -> deviation 0
        var deviation = ConceptLattice.MediationDeviation(
            [1f, 1f], [0f, 0f], [2f, 2f]);
        Assert.Equal(0f, deviation, 5);
    }

    [Fact]
    public void MediationDeviation_GrowsWithDistanceFromMidpoint()
    {
        var centered = ConceptLattice.MediationDeviation(
            [1f, 1f], [0f, 0f], [2f, 2f]);
        var tilted = ConceptLattice.MediationDeviation(
            [0f, 2f], [0f, 0f], [2f, 2f]);
        Assert.True(tilted > centered, "a join tilted toward a pole must deviate more from the midpoint");
    }

    [Fact]
    public void JoinNovelty_JoinCollapsedOntoThesis_ScoresLow()
    {
        // join == thesis: cos(join, thesis)=1, cos(join, antithesis) small -> novelty below the
        // "advances beyond both" bar.
        var thesis = new float[] { 1f, 0f, 0f };
        var antithesis = new float[] { 0f, 1f, 0f };
        var novelty = ConceptLattice.JoinNovelty(thesis, thesis, antithesis);
        Assert.Equal(1f, novelty, 5);
    }

    [Fact]
    public void JoinNovelty_OrthogonalSplitting_MaximisesSum()
    {
        // join orthogonal to both poles -> cos=0 to each -> novelty 0 for unit vectors with this
        // polarity (the S3 objective maximises the *sum*; a pure split adds a new axis).
        var thesis = new float[] { 1f, 0f, 0f };
        var antithesis = new float[] { 0f, 1f, 0f };
        var join = new float[] { 0f, 0f, 1f };
        Assert.Equal(0f, ConceptLattice.JoinNovelty(join, thesis, antithesis), 5);
    }

    [Fact]
    public void JoinNovelty_SymmetricInPoles()
    {
        var thesis = new float[] { 1f, 0.2f };
        var antithesis = new float[] { 0.1f, 1f };
        var join = new float[] { 0.7f, 0.7f };
        Assert.Equal(
            ConceptLattice.JoinNovelty(join, antithesis, thesis),
            ConceptLattice.JoinNovelty(join, thesis, antithesis), 5);
    }

    [Fact]
    public void IsSustainedTheme_TrueWhenThesisAlreadyDominatesJoin()
    {
        // lub(join, thesis) == join ⟺ thesis ≤ join componentwise -> fixed point.
        var join = new float[] { 3f, 3f };
        var thesis = new float[] { 2f, 1f };
        Assert.True(ConceptLattice.IsSustainedTheme(join, thesis));
    }

    [Fact]
    public void IsSustainedTheme_FalseWhenJoinBelowThesisInAnyDimension()
    {
        var join = new float[] { 1f, 3f };
        var thesis = new float[] { 2f, 1f };
        Assert.False(ConceptLattice.IsSustainedTheme(join, thesis));
    }

    [Fact]
    public void IsSustainedTheme_FalseWhenThesisIsNotCovered()
    {
        // thesis has a dimension the join does not reach -> join does not dominate it.
        Assert.False(ConceptLattice.IsSustainedTheme([1f], [1f, 0.5f]));
        Assert.True(ConceptLattice.IsSustainedTheme([1f, 1f], [1f]));
        Assert.False(ConceptLattice.IsSustainedTheme([1f], [0f, 0.2f]));
    }

    [Fact]
    public void IsSustainedTheme_EmptyIntents_AreNotFixedPoints()
    {
        Assert.False(ConceptLattice.IsSustainedTheme([], [1f]));
        Assert.False(ConceptLattice.IsSustainedTheme([1f], []));
    }

    [Fact]
    public void LatticeSemantics_JoinThenSustainCheck_RoundsTripConcepts()
    {
        // thesis ⊔ antithesis with one pole dominating the join is the fixed point the gate
        // detects as "sustained theme" - no movement possible in this vocabulary.
        var thesis = new float[] { 2f, 0.5f };
        var antithesis = new float[] { 1f, 0.1f };
        var join = ConceptLattice.Lub(thesis, antithesis);

        Assert.Equal(ConceptLattice.Lub(join, thesis), join);
        Assert.True(ConceptLattice.IsSustainedTheme(join, thesis));
        Assert.False(ConceptLattice.IsSustainedTheme(join, [3f, 0.5f]));
    }
}