namespace SeldonLattice.Domain.Options;

/// <summary>
/// Per-provider rate limit configuration for outbound LLM calls.
/// Maps provider names to their free-tier RPM limits.
/// Providers not listed here are assumed to have no rate limit (local models).
/// </summary>
public sealed class ProviderRateLimitOptions
{
    /// <summary>Rate limit entries keyed by provider name (case-insensitive).</summary>
    public Dictionary<string, ProviderLimit> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Gemini"] = new() { RequestsPerMinute = 15 },
        ["Groq"] = new() { RequestsPerMinute = 30 },
        ["Grok"] = new() { RequestsPerMinute = 10 },
        ["OpenCodeZen"] = new() { RequestsPerMinute = 20 },
    };

    /// <summary>
    /// Maximum time to wait for a slot before throwing.
    /// Prevents unbounded blocking when the provider is fully saturated.
    /// </summary>
    public int MaxWaitSeconds { get; set; } = 60;
}

/// <summary>
/// Rate limit settings for a single LLM provider.
/// </summary>
public sealed class ProviderLimit
{
    /// <summary>Maximum requests allowed per sliding 60-second window.</summary>
    public int RequestsPerMinute { get; init; } = 60;

    /// <summary>Optional burst limit (max requests in any 1-second window). 0 = no burst limit.</summary>
    public int BurstLimit { get; set; }
}
