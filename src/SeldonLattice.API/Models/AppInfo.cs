namespace SeldonLattice.API.Models;

/// <summary>
/// Tracks application startup time for health check uptime reporting.
/// Uses IHostApplicationLifetime.ApplicationStarted to record the actual start time.
/// </summary>
public sealed class AppInfo(DateTime fallbackTime)
{
    private DateTime _startedAt = fallbackTime;

    public void SetStarted(DateTime utcNow) => _startedAt = utcNow;

    public DateTime StartedAt => _startedAt == default ? DateTime.UtcNow : _startedAt;

    /// <summary>
    /// ISO 8601 timestamp of when the server started.
    /// </summary>
    public string StartTimeIso => StartedAt.ToString("o");

    /// <summary>
    /// Duration since the server started.
    /// </summary>
    public TimeSpan Uptime => DateTime.UtcNow - StartedAt;
}