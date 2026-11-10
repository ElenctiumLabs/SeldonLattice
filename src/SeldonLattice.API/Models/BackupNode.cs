namespace SeldonLattice.API.Models;

public class BackupNode
{
    public string Cid { get; set; } = string.Empty;
    public string NodeType { get; init; } = string.Empty;
    public int Level { get; init; }
    public string Content { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string? SpeakerId { get; init; }
    public DateTime Timestamp { get; init; }
    public float VariabilityScore { get; init; }
    public string? ParentHash { get; init; }
    public string[]? ParentHashes { get; init; }
    public string? ParentPatternHash { get; init; }
    public bool IsSecondaryCenter { get; init; }
    public float CenterShiftScore { get; init; }
    public string CyclePhase { get; init; } = "emergence";
    public float ResonanceScore { get; init; }
    public float ModeratorSelectionConfidence { get; init; }
}