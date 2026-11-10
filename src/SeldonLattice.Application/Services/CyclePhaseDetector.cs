using SeldonLattice.Domain.Options;

namespace SeldonLattice.Application.Services;

/// <summary>
/// Tags a dialogue node's phase based on recent tension dynamics.
/// </summary>
public class CyclePhaseDetector(DialecticsOptions options)
{
    private readonly float _stabilityThreshold = options.StabilityThreshold;
    private readonly int _stabilityWindow = options.StabilityWindow;
    private readonly float _noveltyThreshold = options.NoveltyThreshold;
    private readonly float _variabilityCrisisThreshold = options.VariabilityCrisisThreshold;
    private readonly int _crisisMinTurns = options.CrisisMinTurns;
    private readonly int _growthMaxTurns = options.GrowthMaxTurns;
    private readonly float _velocityPlateauThreshold = options.VelocityPlateauThreshold;
    private readonly float _velocitySaturationThreshold = options.VelocitySaturationThreshold;
    private readonly float _velocityCrisisThreshold = options.VelocityCrisisThreshold;

    /// <summary>
    /// Determines phase from a queue of recent cosine distances.
    /// </summary>
    /// <param name="avgNovelty">Average new argument count over the stability window. High novelty in a stable dialogue indicates productive "renewal" rather than stagnation.</param>
    public string ComputePhase(Queue<float> distanceWindow, float currentVariability, int turnCount, float avgNovelty = 0f)
    {
        if (distanceWindow.Count < _stabilityWindow || distanceWindow.Count == 0)
            return "emergence";

        var mean = distanceWindow.Average();
        var stdDev = (float)Math.Sqrt(distanceWindow.Average(d => (d - mean) * (d - mean)));
        var isStable = stdDev < _stabilityThreshold;

        // Non-stable dialogue in early turns is still growing
        if (!isStable && turnCount < _growthMaxTurns) return "growth";

        // Stable but producing substantial new content = productive tension, not stagnation -
        // even though embeddings have settled into a narrow distance band.
        if (isStable && avgNovelty >= _noveltyThreshold) return "renewal";

        // -- Velocity Vector Calculus (Primitive 2.1) --------------------------
        // When the rate of change of cosine distance between consecutive turns plateaus,
        // use absolute distance to disambiguate the nature of the plateau:
        // - Low distance (<= 0.2): Lexical looping / circular reasoning -> "saturation"
        // - High distance (>= 0.6): Structural deadlock / talking past each other -> "crisis"
        if (distanceWindow.Count >= 2)
        {
            var arr = distanceWindow.ToArray();
            var velocity = Math.Abs(arr[^1] - arr[^2]);
            var absoluteDistance = arr[^1];

            if (velocity < _velocityPlateauThreshold && turnCount >= _crisisMinTurns)
            {
                if (absoluteDistance <= _velocitySaturationThreshold) return "saturation";
                if (absoluteDistance >= _velocityCrisisThreshold) return "crisis";
            }
        }

        // Stable with very low variability after enough turns = stuck in a rut
        if (isStable && currentVariability < _variabilityCrisisThreshold && turnCount > _crisisMinTurns) return "crisis";
        // Stable but still varying = healthy oscillation
        if (isStable && currentVariability >= _variabilityCrisisThreshold) return "renewal";

        // Stable but not varying enough for renewal = saturation; unstable beyond growth window = saturation
        return isStable ? "saturation" : "growth";
    }
}
