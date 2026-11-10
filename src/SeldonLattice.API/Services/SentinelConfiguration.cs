namespace SeldonLattice.API.Services;

/// <summary>
/// Configuration options for Sentinel integration.
/// </summary>
public sealed class SentinelConfiguration
{
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "http://localhost:8080";
    public string ApiKey { get; init; } = string.Empty;
    public SentinelAgentConfig Agent { get; init; } = new();
    public SentinelSecurityConfig Security { get; init; } = new();
    public SentinelTracingConfig Tracing { get; init; } = new();
    public SentinelPricingConfig Pricing { get; init; } = new();
}

/// <summary>
/// USD rates per 1K tokens used to compute the Cost attached to exported
/// traces. Defaults are zero (local models); set them to your provider's
/// rates to get meaningful Cost Intelligence in Sentinel.
/// </summary>
public sealed class SentinelPricingConfig
{
    public decimal InputPer1K { get; init; }
    public decimal OutputPer1K { get; init; }
}

public sealed class SentinelAgentConfig
{
    public string ExternalId { get; init; } = "seldon-lattice";
    public string Name { get; set; } = "Seldon Lattice";
    public string Description { get; set; } = "Dialectical reasoning engine with multi-agent philosophical synthesis";
}

public sealed class SentinelSecurityConfig
{
    public bool Enabled { get; set; } = true;
    public bool FallbackToLocal { get; set; } = true;

    /// <summary>Consecutive Sentinel failures before the breaker opens and scans short-circuit to the local guard.</summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>How long an open breaker stays open before allowing a probe request through.</summary>
    public int OpenDurationSeconds { get; set; } = 60;
}

public sealed class SentinelTracingConfig
{
    public bool Enabled { get; init; } = true;
    public bool IngestOnSessionEnd { get; set; } = true;
}
