using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Domain.Models.Cycle;
using SeldonLattice.Domain.Utilities;

namespace SeldonLattice.Infrastructure.Services;

public abstract partial class DialecticalAscentServiceBase
{    /// <summary>
    /// Result of a single equilibrium-detection step, shared by the non-streaming
    /// and streaming ascent paths so threshold tuning lives in one place.
    /// </summary>
    private sealed record EquilibriumStepResult(
        bool Equilibrium,
        string Phase,
        float Distance,
        float Variability,
        bool BoostTriggered,
        ChatHistory D1History,
        ChatHistory D2History);

    /// <summary>
    /// Single source of truth for the per-turn equilibrium-detection block:
    /// distance/variability window updates, previous-embedding tracking, phase
    /// detection, and the saturation/crisis boost-and-retry logic. Replaces the
    /// previously
    /// duplicated block in <see cref="AscendLevelAsync"/> and the streaming path.
    /// </summary>
    /// <remarks>
    /// Behavior must be preserved exactly - this is a refactor, not a behavior
    /// change. When a boost fires, the rebuilt histories are returned via the
    /// result so the caller can reassign its own history references.
    /// </remarks>
    private EquilibriumStepResult EvaluateEquilibriumStep(
        float[] d1Emb, float[] d2Emb,
        ref float[]? prevD1Emb, ref float[]? prevD2Emb,
        Queue<float> distanceWindow, Queue<float> variabilityWindow,
        ChatHistory d1History, ChatHistory d2History,
        string theme, float avgNovelty, int turn)
    {
        var similarity = SimdVectorUtility.CosineSimilarity(d1Emb, d2Emb);
        var distance = 1 - similarity;
        distanceWindow.Enqueue(distance);
        if (distanceWindow.Count > _stabilityWindow) distanceWindow.Dequeue();

        var variability = 0f;
        if (prevD1Emb != null && prevD2Emb != null)
        {
            var prevSim = SimdVectorUtility.CosineSimilarity(prevD1Emb, prevD2Emb);
            variability = Math.Abs(similarity - prevSim);
        }
        variabilityWindow.Enqueue(variability);
        if (variabilityWindow.Count > _stabilityWindow) variabilityWindow.Dequeue();

        prevD1Emb = d1Emb;
        prevD2Emb = d2Emb;

        bool equilibrium = IsStable(distanceWindow, variabilityWindow, options.StabilityThreshold);

        var phaseCandidate = phaseDetector.ComputePhase(
            distanceWindow,
            variabilityWindow.Count > 0 ? variabilityWindow.Average() : 0f,
            turn,
            avgNovelty);

        var boostTriggered = false;
        if (equilibrium && (phaseCandidate == "saturation" || phaseCandidate == "crisis"))
        {
            equilibrium = false;

            if (_saturationBoostCount == 0)
            {
                _saturationBoostCount++;
                boostTriggered = true;
                logger.LogInformation(
                    "Equilibrium blocked in {Phase} phase. Activating zero-token sampler boost (intensity={Intensity}) - dialogue history preserved.",
                    phaseCandidate, options.SaturationBoostIntensity);
                // NOTE: ascent pressure is now routed through sampler parameters
                // (temperature / top_p / repeat_penalty) via the intensity-aware
                // GetResponseAsync overload in DialecticianBase. The system-prompt
                // hot-swap has been removed to eliminate the Token Displacement Flaw:
                // no extra tokens are injected, so the full dialogue history remains
                // in VRAM at the exact moment the model needs it for breakthrough synthesis.
            }
            else
            {
                logger.LogInformation(
                    "{Phase} persists after boost - continuing with ascent pressure (turn {Turn}/{Max}).",
                    phaseCandidate, turn, _maxTurns);
            }
        }

        return new EquilibriumStepResult(
            equilibrium, phaseCandidate, distance, variability, boostTriggered, d1History, d2History);
    }

}
