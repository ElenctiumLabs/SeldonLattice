using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using SeldonLattice.Domain.Models.Settings;
using SeldonLattice.Domain.Options;

namespace SeldonLattice.API.Services;

/// <summary>
/// The single place that turns deployment defaults (appsettings/environment plus the
/// bound engine options) into a <see cref="FrameworkSettings"/> row.
///
/// Both the settings API (when no DB row exists) and the first-run seeder use this, so
/// the UI, the seeded DB row, and appsettings cannot drift. User-saved DB values remain
/// the runtime source of truth; this class only supplies the baseline.
/// </summary>
public static class SettingsDefaults
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        // The frontend reads these blobs as camelCase. Existing rows written in
        // PascalCase are still readable because EngineConfigService deserialises
        // case-insensitively and the client normalises keys on load.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Canonical role-keyed agent roster used when a row has no AgentsJson yet.</summary>
    public static readonly string DefaultAgentsJson = JsonSerializer.Serialize(
        new Dictionary<string, Dictionary<string, object>>
        {
            ["proposer"] = new() { ["enabled"] = true, ["qualityWeight"] = 1.0 },
            ["skeptic"] = new() { ["enabled"] = true, ["qualityWeight"] = 1.0 },
            ["mediator"] = new() { ["enabled"] = true, ["qualityWeight"] = 1.0 },
            ["shadow"] = new() { ["enabled"] = true, ["qualityWeight"] = 0.5 },
        }, JsonOpts);

    public static FrameworkSettings Create(
        string framework,
        IConfiguration config,
        QualityGateOptions qualityGates,
        DialecticsOptions dialectics)
    {
        var provider = config["LlmProvider"];
        if (string.IsNullOrWhiteSpace(provider))
            provider = "OpenAI";

        var providerSection = config.GetSection(provider);
        var frontend = config.GetSection("FrontendConfig");

        return new FrameworkSettings
        {
            Framework = framework.ToLowerInvariant(),
            // Seeding is not a user save. The repository preserves this value, and the
            // settings controller sets it true on the first deliberate Save.
            IsConfigured = false,
            Provider = provider,
            Model = providerSection["Model"] ?? "",
            ApiEndpoint = providerSection["Endpoint"] ?? "",
            ApiKey = providerSection["ApiKey"] ?? "",
            AuthToken = "",
            RateLimit = frontend.GetValue("DefaultRateLimit", 100),
            LayoutMode = "helical",
            MaxDepth = frontend.GetValue("DefaultMaxDepth", 10),
            QualityThreshold = frontend.GetValue("DefaultQualityThreshold", 0.7),
            EnableStreamingSSE = true,
            UseStaticQuestions = true,
            EnableThinking = config.GetValue("HttpClient:Llm:EnableThinking", false),
            PersistReasoningTrace = config.GetValue("HttpClient:Llm:PersistReasoningTrace", false),
            FontSize = "medium",
            EnableAnimations = true,
            SessionMaxDepth = frontend.GetValue("DefaultMaxDepth", 10),
            AutoArchiveDays = frontend.GetValue("DefaultAutoArchiveDays", 30),
            SessionPrefix = "",
            ExportFormat = "markdown",
            IncludeMerkleProofs = true,
            IncludeMetrics = true,
            ComplianceReport = false,
            NotifyOnSynthesis = true,
            NotifyOnGateRejection = true,
            NotifyOnSessionStall = true,
            NotifyOnMaxDepth = true,
            NotificationEmail = "",
            WebhookUrl = "",
            AgentsJson = DefaultAgentsJson,
            QualityGatesJson = JsonSerializer.Serialize(qualityGates, JsonOpts),
            DialecticsJson = JsonSerializer.Serialize(dialectics, JsonOpts),
        };
    }

    /// <summary>
    /// Provider section names and their deployment defaults, derived from the
    /// appsettings section named in <c>Settings:Providers</c>. The frontend uses this
    /// instead of its own hardcoded provider map.
    /// </summary>
    public static IReadOnlyDictionary<string, ProviderDefault> ProviderDefaults(IConfiguration config)
    {
        var providers = config.GetSection("Settings:Providers").Get<string[]>() ?? [];
        var result = new Dictionary<string, ProviderDefault>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providers)
        {
            var section = config.GetSection(provider);
            result[provider] = new ProviderDefault(
                section["Model"] ?? "",
                section["Endpoint"] ?? "");
        }

        return result;
    }

    public sealed record ProviderDefault(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("endpoint")] string Endpoint);
}
