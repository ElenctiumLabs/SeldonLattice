namespace SeldonLattice.Domain.Models.Cycle;

/// <summary>
/// Tracks the ongoing state of dialogue quality across turns.
/// </summary>
public class DialogueQualityState
{
    /// <summary>
    /// Full response history for the current dialogue session.
    /// </summary>
    public List<string> ResponseHistory { get; } = [];

    /// <summary>
    /// Consecutive turns with quality score below threshold.
    /// </summary>
    public int ConsecutiveLowQualityTurns { get; set; }

    /// <summary>
    /// Quality metrics for each turn, indexed by turn number.
    /// </summary>
    private List<ResponseQualityMetrics> TurnMetrics { get; } = [];

    /// <summary>
    /// Consecutive responses whose embedding similarity to the root theme fell below the
    /// relevance threshold. Reset to zero whenever a response returns on-topic.
    /// </summary>
    public int ConsecutiveOffTopicTurns { get; set; }

    public void RecordTurn(string response, ResponseQualityMetrics metrics, double qualityThreshold)
    {
        ResponseHistory.Add(response);
        TurnMetrics.Add(metrics);

        if (metrics.QualityScore < qualityThreshold)
            ConsecutiveLowQualityTurns++;
        else
            ConsecutiveLowQualityTurns = 0;
    }
}