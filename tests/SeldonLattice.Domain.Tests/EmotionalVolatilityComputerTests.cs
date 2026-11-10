using SeldonLattice.Domain.Models;
using SeldonLattice.Domain.Models.Cycle;
using SeldonLattice.Domain.Utilities;
using Xunit;

namespace SeldonLattice.Domain.Tests;

public class EmotionalVolatilityComputerTests
{
    [Fact]
    public void Compute_BothMetricsNull_ReturnsNeutralState()
    {
        var evl = EmotionalVolatilityComputer.Compute(
            ownMetrics: null,
            opponentMetrics: null,
            previousQualityScore: 50.0,
            phase: "emergence",
            turn: 0);

        Assert.Equal(EmotionalVolatilityState.Neutral.Arousal, evl.Arousal);
        Assert.Equal(EmotionalVolatilityState.Neutral.Valence, evl.Valence);
        Assert.Equal(EmotionalVolatilityState.Neutral.Momentum, evl.Momentum);
        Assert.Equal(EmotionalVolatilityState.Neutral.Provocation, evl.Provocation);
    }

    [Fact]
    public void Compute_HighOpponentNoveltyAndLowSimilarity_IncreasesArousal()
    {
        var own = new ResponseQualityMetrics
        {
            QualityScore = 70.0,
            SimilarityToPrevious = 0.2, // Low similarity = genuine divergence
            NewArgumentCount = 3,
        };
        var opponent = new ResponseQualityMetrics
        {
            QualityScore = 75.0,
            CeremonyScore = 0.0,
            NewArgumentCount = 4,
        };

        var evl = EmotionalVolatilityComputer.Compute(own, opponent, 65.0, "emergence", 2);

        Assert.True(evl.Arousal > 0.55, $"Expected high arousal, got {evl.Arousal}");
    }

    [Fact]
    public void Compute_CrisisPhase_DampensArousalComparedToEmergence()
    {
        var own = new ResponseQualityMetrics { QualityScore = 60.0, SimilarityToPrevious = 0.5 };
        var opponent = new ResponseQualityMetrics { QualityScore = 60.0, CeremonyScore = 0.5, NewArgumentCount = 1 };

        var emergenceEvl = EmotionalVolatilityComputer.Compute(own, opponent, 60.0, "emergence", 3);
        var crisisEvl = EmotionalVolatilityComputer.Compute(own, opponent, 60.0, "crisis", 3);

        Assert.True(crisisEvl.Arousal < emergenceEvl.Arousal, "Crisis phase should dampen arousal via saturation pressure");
    }

    [Fact]
    public void Compute_OwnArgumentsLandedDominating_ProducesPositiveValence()
    {
        var own = new ResponseQualityMetrics
        {
            QualityScore = 80.0,
            NewArgumentCount = 5,
        };
        var opponent = new ResponseQualityMetrics
        {
            QualityScore = 50.0,
            CeremonyScore = 0.9, // high ceremony = low pressure
            NewArgumentCount = 0,
        };

        var evl = EmotionalVolatilityComputer.Compute(own, opponent, 70.0, "growth", 3);

        Assert.True(evl.Valence > 0.0, $"Expected positive valence, got {evl.Valence}");
    }

    [Fact]
    public void Compute_HighOpponentPressureWithoutCeremony_ProducesNegativeValenceAndHighProvocation()
    {
        var own = new ResponseQualityMetrics
        {
            QualityScore = 50.0,
            NewArgumentCount = 0,
        };
        var opponent = new ResponseQualityMetrics
        {
            QualityScore = 85.0,
            CeremonyScore = 0.0, // direct attack
            NewArgumentCount = 4,
        };

        var evl = EmotionalVolatilityComputer.Compute(own, opponent, 50.0, "growth", 3);

        Assert.True(evl.Valence < 0.0, $"Expected negative valence under direct pressure, got {evl.Valence}");
        Assert.True(evl.Provocation > 0.5, $"Expected high provocation from direct opponent attack, got {evl.Provocation}");
    }

    [Fact]
    public void Compute_QualityScoreImprovement_ProducesPositiveMomentum()
    {
        var own = new ResponseQualityMetrics
        {
            QualityScore = 85.0,
        };

        var evl = EmotionalVolatilityComputer.Compute(own, null, 60.0, "growth", 2);

        Assert.True(evl.Momentum > 0.0, $"Expected positive momentum, got {evl.Momentum}");
        Assert.Equal((85.0 - 60.0) / 50.0, evl.Momentum, 2);
    }

    [Fact]
    public void Compute_QualityScoreDegradation_ProducesNegativeMomentum()
    {
        var own = new ResponseQualityMetrics
        {
            QualityScore = 40.0,
        };

        var evl = EmotionalVolatilityComputer.Compute(own, null, 70.0, "saturation", 4);

        Assert.True(evl.Momentum < 0.0, $"Expected negative momentum, got {evl.Momentum}");
        Assert.Equal((40.0 - 70.0) / 50.0, evl.Momentum, 2);
    }

    [Fact]
    public void IsSignificant_ArousalAboveThreshold_ReturnsTrue()
    {
        var state = new EmotionalVolatilityState
        {
            Arousal = 0.6,
            Momentum = 0.0,
        };

        Assert.True(state.IsSignificant(0.0));
    }

    [Fact]
    public void IsSignificant_MomentumDeltaAboveThreshold_ReturnsTrue()
    {
        var state = new EmotionalVolatilityState
        {
            Arousal = 0.4,
            Momentum = 0.3,
        };

        Assert.True(state.IsSignificant(0.1)); // |0.3 - 0.1| = 0.2 > 0.15
    }

    [Fact]
    public void IsSignificant_CalmEarlyTurn_ReturnsFalse()
    {
        var state = new EmotionalVolatilityState
        {
            Arousal = 0.4,
            Momentum = 0.05,
        };

        Assert.False(state.IsSignificant(0.0)); // Arousal <= 0.55 and |0.05 - 0.0| <= 0.15
    }
}
