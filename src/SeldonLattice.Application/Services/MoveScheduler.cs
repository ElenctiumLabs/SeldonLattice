using SeldonLattice.Domain.Models;
using SeldonLattice.Domain.Models.Cycle;

namespace SeldonLattice.Application.Services;

/// <summary>
/// Offers a single optional rhetorical angle when recent response metrics indicate
/// repetition without novelty. Selection is signal-driven, not a phase or turn schedule.
/// </summary>
public static class MoveScheduler
{
    /// <summary>
    /// Suggests a move only when the previous response introduced no new argument and
    /// repeated the same speaker's earlier material. Similarity to the opposing speaker
    /// is not treated as repetition because overlap can be legitimate. Returns null when
    /// a move is not indicated.
    /// </summary>
    public static RhetoricalMove? Suggest(ResponseQualityMetrics? previousMetrics)
    {
        if (previousMetrics is null || previousMetrics.NewArgumentCount > 0)
            return null;

        if (previousMetrics.SelfSimilarity > 0.35)
            return RhetoricalMove.FormalDistinction;

        return null;
    }
}
