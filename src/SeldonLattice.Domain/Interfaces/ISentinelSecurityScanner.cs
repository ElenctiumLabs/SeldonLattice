namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Abstraction for prompt injection detection. When Sentinel is enabled,
/// this delegates to Sentinel's policy evaluation. When disabled or unavailable,
/// falls back to local regex-based detection.
/// </summary>
public interface ISentinelSecurityScanner
{
    /// <summary>
    /// Scans text for prompt injection threats.
    /// Returns a result indicating whether a threat was detected.
    /// </summary>
    Task<SentinelSecurityScanResult> ScanTextAsync(
        string? text,
        string? sessionId = null,
        string? speakerId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a security scan from Sentinel or local guard.
/// </summary>
public sealed class SentinelSecurityScanResult
{
    public bool IsThreatDetected { get; init; }
    public string ThreatType { get; set; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string[] MatchedPatterns { get; set; } = [];

    public static SentinelSecurityScanResult Clean { get; } = new();
}
