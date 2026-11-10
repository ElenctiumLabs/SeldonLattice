using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeldonLattice.API.Services;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Settings;
using SeldonLattice.Domain.Options;
using SeldonLattice.Domain.Validation;

namespace SeldonLattice.API.Controllers;

/// <summary>
/// CRUD endpoints for per-framework user settings backed by SQLite.
/// Resolution order for every field:
///   1. Saved DB value  (FrameworkSettings row)
///   2. appsettings.json / environment variable equivalent
///   3. Hard-coded last-resort default
/// </summary>
/// <remarks>
/// Requires the <c>settings</c> role (policy "SettingsOnly"). Stored LLM credentials
/// (ApiKey, AuthToken) are write-only: GET redacts them to empty strings and POST treats
/// empty/null as "unchanged" - they never leave the server.
/// </remarks>
[ApiController]
[Route("api/settings")]
[Authorize(Policy = "SettingsOnly")]
public class SettingsController(
    ISettingsRepository repo,
    IEngineConfigService engineConfigService,
    IConfiguration config,
    LlmOptions llmOptions)
    : ControllerBase
{
    private static readonly HashSet<string> ValidFrameworks =
        new(StringComparer.OrdinalIgnoreCase) { "philosophy", "research" };

    /// <summary>
    /// Returns whether the framework has been explicitly configured by the user.
    /// </summary>
    [HttpGet("{framework}/status")]
    public async Task<IResult> GetStatus(string framework, CancellationToken ct)
    {
        if (!ValidFrameworks.Contains(framework))
            return TypedResults.BadRequest($"Unknown framework '{framework}'. Valid values: philosophy, research.");

        var configured = await repo.IsConfiguredAsync(framework, ct);
        return TypedResults.Ok(new StatusResponse(configured));
    }

    /// <summary>
    /// Returns the current settings for the framework. Falls back to appsettings.json values when no DB row exists.
    /// </summary>
    [HttpGet("{framework}")]
    public async Task<IResult> GetSettings(string framework, CancellationToken ct)
    {
        if (!ValidFrameworks.Contains(framework))
            return TypedResults.BadRequest($"Unknown framework '{framework}'.");

        // The DB is the only runtime source. Appsettings/defaults are used by the seeder
        // on first run only; a missing or incomplete row is a loud error.
        var entity = await repo.GetAsync(framework, ct);
        if (entity is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Settings missing",
                detail: $"No settings row exists for framework '{framework}'. Restart the API so first-run seeding can create it.");
        }

        var errors = FrameworkSettingsValidator.Validate(entity);
        if (errors.Count > 0)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Settings incomplete",
                detail: FrameworkSettingsValidator.Describe(errors));
        }

        return TypedResults.Ok(ToDto(entity));
    }

    /// <summary>
    /// Returns the provider catalog and deployment defaults (model/endpoint) from
    /// appsettings, so the frontend does not carry its own provider map.
    /// </summary>
    [HttpGet("providers")]
    public IActionResult GetProviders()
    {
        var providers = SettingsDefaults.ProviderDefaults(config);
        if (providers.Count == 0)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Provider catalog missing",
                detail: "No providers are configured. Add Settings:Providers to appsettings.");
        }

        return Ok(providers);
    }

    /// <summary>
    /// Creates or updates the settings for the framework. The frontend posts a full snapshot;
    /// booleans and numbers are applied as sent. Empty credentials mean "keep the stored secret".
    /// </summary>
    [HttpPost("{framework}")]
    public async Task<IResult> SaveSettings(string framework, [FromBody] SettingsDto dto, CancellationToken ct)
    {
        if (!ValidFrameworks.Contains(framework))
            return TypedResults.BadRequest($"Unknown framework '{framework}'.");

        // The DB row must exist. It is created by first-run seeding, not by a save.
        var existing = await repo.GetAsync(framework, ct);
        if (existing is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Settings missing",
                detail: $"No settings row exists for framework '{framework}'. Restart the API so first-run seeding can create it.");
        }

        ApplyDto(existing, dto, config, framework);

        var errors = FrameworkSettingsValidator.Validate(existing);
        if (errors.Count > 0)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Settings invalid",
                detail: FrameworkSettingsValidator.Describe(errors));
        }

        // A POST is a deliberate user save, unlike the first-run seed.
        existing.IsConfigured = true;

        var saved = await repo.UpsertAsync(existing, ct);

        // Invalidate cached engine config and copy the saved values into the live
        // singleton options. Agents hold references to those instances, so the next
        // ascent sees the new quality gates and dialectics without a restart.
        engineConfigService.InvalidateCache(framework);
        await engineConfigService.ApplyRuntimeOptionsAsync(framework, ct);

        // Apply LlmOptions to the singleton so OpenAICompatibleChatCompletionService sees the change.
        llmOptions.EnableThinking = saved.EnableThinking;
        llmOptions.PersistReasoningTrace = saved.PersistReasoningTrace;

        return TypedResults.Ok(ToDto(saved));
    }

    // ---- Mapping helpers --------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Apply the posted snapshot onto an existing entity. Strings merge only when a real
    /// value was sent; booleans and numerics are the snapshot's values.
    /// </summary>
    private static void ApplyDto(FrameworkSettings entity, SettingsDto dto, IConfiguration config, string framework)
    {
        // Connection - only overwrite if the client sent a real value.
        // ApiKey/AuthToken are write-only (D1): the client receives "" from GET, and an
        // empty/null value on POST means "keep the stored credential".
        if (!string.IsNullOrWhiteSpace(dto.Provider))    entity.Provider    = dto.Provider;
        if (!string.IsNullOrWhiteSpace(dto.Model))       entity.Model       = dto.Model;
        if (!string.IsNullOrWhiteSpace(dto.ApiEndpoint)) entity.ApiEndpoint = dto.ApiEndpoint;
        if (!string.IsNullOrEmpty(dto.ApiKey))           entity.ApiKey      = dto.ApiKey;
        if (!string.IsNullOrEmpty(dto.AuthToken))        entity.AuthToken   = dto.AuthToken;
        if (dto.RateLimit > 0)                           entity.RateLimit   = dto.RateLimit;
        // New: persist user name if provided
        if (dto.UserName is not null)                    entity.UserName    = dto.UserName.Trim();

        // Engine
        if (!string.IsNullOrWhiteSpace(dto.LayoutMode)) entity.LayoutMode = dto.LayoutMode;
        if (dto.MaxDepth > 0)                            entity.MaxDepth   = dto.MaxDepth;
        if (dto.QualityThreshold > 0)                   entity.QualityThreshold = dto.QualityThreshold;
        entity.EnableStreamingSSE  = dto.EnableStreamingSSE;
        entity.UseStaticQuestions  = dto.UseStaticQuestions;
        entity.EnableThinking       = dto.EnableThinking;
        entity.PersistReasoningTrace = dto.PersistReasoningTrace;

        // Display
        if (!string.IsNullOrWhiteSpace(dto.FontSize)) entity.FontSize = dto.FontSize;
        entity.EnableAnimations = dto.EnableAnimations;

        // Session
        if (dto.SessionMaxDepth > 0)    entity.SessionMaxDepth  = dto.SessionMaxDepth;
        if (dto.AutoArchiveDays > 0)    entity.AutoArchiveDays  = dto.AutoArchiveDays;
        if (dto.SessionPrefix is not null) entity.SessionPrefix = dto.SessionPrefix;

        // Export
        if (!string.IsNullOrWhiteSpace(dto.ExportFormat)) entity.ExportFormat = dto.ExportFormat;
        entity.IncludeMerkleProofs = dto.IncludeMerkleProofs;
        entity.IncludeMetrics      = dto.IncludeMetrics;
        entity.ComplianceReport    = dto.ComplianceReport;

        // Notifications
        entity.NotifyOnSynthesis      = dto.NotifyOnSynthesis;
        entity.NotifyOnGateRejection  = dto.NotifyOnGateRejection;
        entity.NotifyOnSessionStall   = dto.NotifyOnSessionStall;
        entity.NotifyOnMaxDepth       = dto.NotifyOnMaxDepth;
        if (dto.NotificationEmail is not null) entity.NotificationEmail = dto.NotificationEmail;
        if (dto.WebhookUrl        is not null) entity.WebhookUrl        = dto.WebhookUrl;

        // Agent / Engine config JSON blobs - only replace if the client sent a non-trivial value
        if (!string.IsNullOrWhiteSpace(dto.AgentsJson) && dto.AgentsJson != "{}")
            entity.AgentsJson = dto.AgentsJson;
        if (dto.QualityGatesJson is { Length: > 2 })
            entity.QualityGatesJson = dto.QualityGatesJson;
        if (dto.DialecticsJson is { Length: > 2 })
            entity.DialecticsJson = dto.DialecticsJson;
    }

    private static SettingsDto ToDto(FrameworkSettings e) => new()
    {
        Framework          = e.Framework,
        IsConfigured       = e.IsConfigured,
        UserName           = e.UserName,
        Provider           = e.Provider,
        Model              = e.Model,
        ApiEndpoint        = e.ApiEndpoint,
        // Write-only credentials: never leave the server. POST treats an empty value as
        // "keep the stored secret".
        ApiKey             = "",
        AuthToken          = "",
        HasApiKey          = !string.IsNullOrEmpty(e.ApiKey),
        HasAuthToken       = !string.IsNullOrEmpty(e.AuthToken),
        RateLimit          = e.RateLimit,
        LayoutMode         = e.LayoutMode,
        MaxDepth           = e.MaxDepth,
        QualityThreshold   = e.QualityThreshold,
        EnableStreamingSSE = e.EnableStreamingSSE,
        UseStaticQuestions = e.UseStaticQuestions,
        EnableThinking = e.EnableThinking,
        PersistReasoningTrace = e.PersistReasoningTrace,
        FontSize           = e.FontSize,
        EnableAnimations   = e.EnableAnimations,
        SessionMaxDepth    = e.SessionMaxDepth,
        AutoArchiveDays    = e.AutoArchiveDays,
        SessionPrefix      = e.SessionPrefix,
        ExportFormat       = e.ExportFormat,
        IncludeMerkleProofs = e.IncludeMerkleProofs,
        IncludeMetrics     = e.IncludeMetrics,
        ComplianceReport   = e.ComplianceReport,
        NotifyOnSynthesis  = e.NotifyOnSynthesis,
        NotifyOnGateRejection = e.NotifyOnGateRejection,
        NotifyOnSessionStall  = e.NotifyOnSessionStall,
        NotifyOnMaxDepth      = e.NotifyOnMaxDepth,
        NotificationEmail  = e.NotificationEmail,
        WebhookUrl         = e.WebhookUrl,
        AgentsJson         = e.AgentsJson,
        QualityGatesJson   = e.QualityGatesJson,
        DialecticsJson     = e.DialecticsJson,
        UpdatedAt          = e.UpdatedAt,
    };

    // ---- DTOs ------------------------------------------------------------------------------------------------------------------------------------

    private record StatusResponse(bool IsConfigured);

    public sealed class SettingsDto
    {
        public string? Framework    { get; set; }
        public bool IsConfigured    { get; init; }
        public string? UserName     { get; init; }

        // Connection
        public string? Provider     { get; init; }
        public string? Model        { get; init; }
        public string? ApiEndpoint  { get; init; }
        public string? ApiKey       { get; init; }
        public string? AuthToken    { get; init; }
        /// <summary>True when a credential is stored; the value itself is never returned.</summary>
        public bool HasApiKey       { get; init; }
        /// <summary>True when a session token is stored; the value itself is never returned.</summary>
        public bool HasAuthToken    { get; init; }
        public int RateLimit        { get; init; } = 100;

        // Engine
        public string? LayoutMode       { get; init; }
        public int MaxDepth             { get; init; } = 10;
        public double QualityThreshold  { get; init; } = 0.7;
        public bool EnableStreamingSSE  { get; init; } = true;
        public bool UseStaticQuestions  { get; init; } = true;
        public bool EnableThinking       { get; init; }
        public bool PersistReasoningTrace { get; init; }

        // Display
        public string? FontSize         { get; init; }
        public bool EnableAnimations    { get; init; } = true;

        // Session defaults
        public int SessionMaxDepth      { get; init; } = 10;
        public int AutoArchiveDays      { get; init; } = 30;
        public string? SessionPrefix    { get; init; }

        // Export
        public string? ExportFormat         { get; init; }
        public bool IncludeMerkleProofs     { get; init; } = true;
        public bool IncludeMetrics          { get; init; } = true;
        public bool ComplianceReport        { get; init; }

        // Notifications
        public bool NotifyOnSynthesis       { get; init; } = true;
        public bool NotifyOnGateRejection   { get; init; } = true;
        public bool NotifyOnSessionStall    { get; init; } = true;
        public bool NotifyOnMaxDepth        { get; init; } = true;
        public string? NotificationEmail    { get; init; }
        public string? WebhookUrl           { get; init; }

        // Agents
        public string? AgentsJson           { get; init; }

        // Engine Config JSON blobs
        public string? QualityGatesJson     { get; init; }
        public string? DialecticsJson       { get; init; }

        public DateTime UpdatedAt           { get; set; }
    }
}
