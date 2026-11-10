using SeldonLattice.Application.Services;
using SeldonLattice.Domain.Options;
using Xunit;

namespace SeldonLattice.Application.Tests;

/// <summary>
/// Unit tests for CyclePhaseDetector, verifying tension dynamic phase classification
/// including the Velocity Vector Calculus (Primitive 2.1).
/// </summary>
public class CyclePhaseDetectorTests
{
    private readonly DialecticsOptions _options = new()
    {
        StabilityThreshold = 0.08f,
        StabilityWindow = 3,
        NoveltyThreshold = 1.0f,
        VariabilityCrisisThreshold = 0.1f,
        CrisisMinTurns = 6,
        GrowthMaxTurns = 4,
        VelocityPlateauThreshold = 0.05f,
        VelocitySaturationThreshold = 0.20f,
        VelocityCrisisThreshold = 0.60f,
    };

    [Fact]
    public void ComputePhase_WindowBelowStabilityWindow_ReturnsEmergence()
    {
        var detector = new CyclePhaseDetector(_options);
        var window = new Queue<float>([0.5f, 0.4f]);

        var phase = detector.ComputePhase(window, currentVariability: 0.1f, turnCount: 1);

        Assert.Equal("emergence", phase);
    }

    [Fact]
    public void ComputePhase_UnstableEarlyTurns_ReturnsGrowth()
    {
        var detector = new CyclePhaseDetector(_options);
        // High standard deviation > 0.08, turnCount < 4
        var window = new Queue<float>([0.1f, 0.8f, 0.2f]);

        var phase = detector.ComputePhase(window, currentVariability: 0.3f, turnCount: 2);

        Assert.Equal("growth", phase);
    }

    [Fact]
    public void ComputePhase_StableWithHighNovelty_ReturnsRenewal()
    {
        var detector = new CyclePhaseDetector(_options);
        // Standard deviation < 0.08
        var window = new Queue<float>([0.30f, 0.31f, 0.30f]);

        var phase = detector.ComputePhase(window, currentVariability: 0.05f, turnCount: 7, avgNovelty: 1.5f);

        Assert.Equal("renewal", phase);
    }

    [Fact]
    public void ComputePhase_VelocityPlateauLowDistance_ReturnsSaturation()
    {
        var detector = new CyclePhaseDetector(_options);
        // Delta between last two distances is |0.10 - 0.11| = 0.01 < 0.05 (velocity plateau)
        // Absolute distance = 0.10 <= 0.20 (saturation)
        // turnCount >= 6
        var window = new Queue<float>([0.11f, 0.11f, 0.10f]);

        var phase = detector.ComputePhase(window, currentVariability: 0.15f, turnCount: 7);

        Assert.Equal("saturation", phase);
    }

    [Fact]
    public void ComputePhase_VelocityPlateauHighDistance_ReturnsCrisis()
    {
        var detector = new CyclePhaseDetector(_options);
        // Delta between last two distances is |0.75 - 0.76| = 0.01 < 0.05 (velocity plateau)
        // Absolute distance = 0.75 >= 0.60 (crisis)
        // turnCount >= 6
        var window = new Queue<float>([0.76f, 0.76f, 0.75f]);

        var phase = detector.ComputePhase(window, currentVariability: 0.15f, turnCount: 7);

        Assert.Equal("crisis", phase);
    }
}
