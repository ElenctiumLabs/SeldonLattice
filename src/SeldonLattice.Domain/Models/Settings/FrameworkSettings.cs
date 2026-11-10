namespace SeldonLattice.Domain.Models.Settings;

/// <summary>
/// Represents the persisted configuration for a single dialectical framework
/// (philosophy or research). Stored in SQLite via EF Core.
/// </summary>
public sealed class FrameworkSettings
{
    public int Id { get; init; }

    /// <summary>Framework key: "philosophy" | "research"</summary>
    public required string Framework { get; set; }

    /// <summary>
    /// Set to true after the first deliberate Save by the user, used by the
    /// frontend gate to decide whether to show the settings page on entry.
    /// </summary>
    public bool IsConfigured { get; set; }
    
    /// <summary>
    /// The user's name for agents to address them by.
    /// </summary>
    public string UserName { get; set; } = "";

    //  LLM Connection 

    public string Provider { get; set; } = "LlamaCpp";
    public string Model { get; set; } = "Qwen3-30B-A3B-Instruct-2507-Q4_K_M";
    public string ApiEndpoint { get; set; } = "http://localhost:9931/v1";
    public string ApiKey { get; set; } = "llama-cpp";
    public string AuthToken { get; set; } = "";
    public int RateLimit { get; set; } = 100;

    //  Reasoning Engine 

    public string LayoutMode { get; set; } = "helical"; // "helical" | "dialectical"
    public int MaxDepth { get; set; } = 10;
    public double QualityThreshold { get; set; } = 0.7;
    public bool EnableStreamingSSE { get; set; } = true;
    public bool UseStaticQuestions { get; set; } = true;
    public bool EnableThinking { get; set; } = false;
    public bool PersistReasoningTrace { get; set; } = false;

    //  Display 

    public string FontSize { get; set; } = "medium"; // "small" | "medium" | "large"
    public bool EnableAnimations { get; set; } = true;

    //  Session Defaults 

    public int SessionMaxDepth { get; set; } = 10;
    public int AutoArchiveDays { get; set; } = 30;
    public string SessionPrefix { get; set; } = "";

    //  Export 

    public string ExportFormat { get; set; } = "markdown"; // "markdown" | "pdf" | "json"
    public bool IncludeMerkleProofs { get; set; } = true;
    public bool IncludeMetrics { get; set; } = true;
    public bool ComplianceReport { get; set; } = false;

    //  Notifications 

    public bool NotifyOnSynthesis { get; set; } = true;
    public bool NotifyOnGateRejection { get; set; } = true;
    public bool NotifyOnSessionStall { get; set; } = true;
    public bool NotifyOnMaxDepth { get; set; } = true;
    public string NotificationEmail { get; set; } = "";
    public string WebhookUrl { get; set; } = "";

    //  Agent Config (serialised as JSON) 

    /// <summary>
    /// JSON blob: Record&lt;string, { enabled: bool, qualityWeight: double }&gt;
    /// Stored as a JSON string to avoid an extra junction table.
    /// </summary>
    public string AgentsJson { get; set; } = "{}";

    //  Engine Config (serialised as JSON) 

    /// <summary>
    /// JSON blob representing a QualityGateOptions override for this framework.
    /// When null or empty, the application falls back to the seeded defaults
    /// stored in the DB at first run (from appsettings.json values).
    /// </summary>
    public string? QualityGatesJson { get; set; }

    /// <summary>
    /// JSON blob representing a DialecticsOptions override for this framework.
    /// When null or empty, the application falls back to the seeded defaults
    /// stored in the DB at first run (from appsettings.json values).
    /// The stored engine config JSON (quality gates, dialectics) lives alongside these columns.
    /// </summary>
    public string? DialecticsJson { get; set; }

    //  Audit 

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
