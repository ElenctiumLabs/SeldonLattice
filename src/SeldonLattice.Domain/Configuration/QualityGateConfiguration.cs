namespace SeldonLattice.Domain.Configuration;

/// <summary>
/// Represents the quality gate thresholds used to detect stalemate, ceremony, and circular reasoning in debates.
/// </summary>
public class QualityGateConfiguration
{
    public bool Enabled { get; set; }
    public int MaxTurns { get; set; }
    public int ConsecutiveFarewellsThreshold { get; set; }
    public double SimilarityThreshold { get; set; }
    public int AgreementStagnationTurns { get; set; }
    public double CeremonyScoreWarning { get; set; }
    public int VerboseWordCount { get; set; }
    public double ContentRepetitionWarning { get; set; }
    public double QualityScoreExitThreshold { get; set; }
    public int LowQualityConsecutiveTurns { get; set; }
    public double CircularRestatementThreshold { get; set; }
    public double PremiseForkDistanceThreshold { get; set; }
    public int PremiseForkSustainedTurns { get; set; }
    public DateTime UpdatedAt { get; set; }
}