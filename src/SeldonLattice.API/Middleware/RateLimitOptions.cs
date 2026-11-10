namespace SeldonLattice.API.Middleware;

/// <summary>
/// Options for configuring the in-memory rate limiter.
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>Maximum number of requests allowed per window per client.</summary>
    public static int RequestsPerMinute => 60;

    /// <summary>Burst limit - maximum requests allowed in a single second.</summary>
    public static int BurstLimit => 0;

    /// <summary>Sliding window size in seconds (default: 60).</summary>
    public static int WindowSizeSeconds => 60;
}