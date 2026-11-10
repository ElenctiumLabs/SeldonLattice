namespace SeldonLattice.Application.Security;

/// <summary>
/// Result of a prompt injection security scan.
/// </summary>
public record SecurityScanResult
{
    public bool IsThreatDetected { get; init; }
    public string ThreatType { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string[] MatchedPatterns { get; init; } = [];

    public static SecurityScanResult Clean => new SecurityScanResult
    {
        IsThreatDetected = false,
        ThreatType = "Clean",
        Details = "No injection patterns detected."
    };
}