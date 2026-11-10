using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Options;

namespace SeldonLattice.Infrastructure.Settings;

/// <summary>
/// Resolves QualityGateOptions and DialecticsOptions for a given framework from the SQLite
/// settings store, with a short memory-cache TTL to avoid per-request DB hits.
///
/// The DB is the only runtime source. There is no fallback to constructor-injected defaults:
/// appsettings/bound options are used by the first-run seeder only. A missing row or missing
/// engine JSON throws, so the failure is loud instead of silently running on stale values.
///
/// Call <see cref="InvalidateCache"/> after saving new settings to pick up changes immediately.
/// </summary>
public sealed class EngineConfigService(
    ISettingsRepository repo,
    IMemoryCache cache,
    ILogger<EngineConfigService> logger,
    QualityGateOptions defaultQualityGates,
    DialecticsOptions defaultDialectics)
    : IEngineConfigService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    // -- IEngineConfigService --------------------------------------------------

    public async Task<QualityGateOptions> GetQualityGateOptionsAsync(string framework, CancellationToken ct = default)
    {
        var cacheKey = $"qg:{framework.ToLowerInvariant()}";
        if (cache.TryGetValue(cacheKey, out QualityGateOptions? cached) && cached is not null)
            return cached;

        var settings = await repo.GetAsync(framework, ct)
            ?? throw new InvalidOperationException(
                $"No settings row exists for framework '{framework}'.");

        if (string.IsNullOrWhiteSpace(settings.QualityGatesJson))
            throw new InvalidOperationException(
                $"QualityGatesJson is missing for framework '{framework}'.");

        QualityGateOptions result;
        try
        {
            result = JsonSerializer.Deserialize<QualityGateOptions>(settings.QualityGatesJson, JsonOpts)
                ?? throw new InvalidOperationException(
                    $"QualityGatesJson for framework '{framework}' deserialised to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"QualityGatesJson for framework '{framework}' is invalid: {ex.Message}", ex);
        }

        if (result.MaxTurns <= 0 ||
            result.SimilarityThreshold <= 0 ||
            result.QualityScoreExitThreshold <= 0)
        {
            throw new InvalidOperationException(
                $"QualityGatesJson for framework '{framework}' has non-positive required values.");
        }

        cache.Set(cacheKey, result, CacheTtl);
        return result;
    }

    public async Task<DialecticsOptions> GetDialecticsOptionsAsync(string framework, CancellationToken ct = default)
    {
        var key = framework.ToLowerInvariant();
        var cacheKey = $"dial:{key}";
        if (cache.TryGetValue(cacheKey, out DialecticsOptions? cached) && cached is not null)
            return cached;

        var settings = await repo.GetAsync(framework, ct)
            ?? throw new InvalidOperationException(
                $"No settings row exists for framework '{framework}'.");

        if (string.IsNullOrWhiteSpace(settings.DialecticsJson))
            throw new InvalidOperationException(
                $"DialecticsJson is missing for framework '{framework}'.");

        DialecticsOptions result;
        try
        {
            result = JsonSerializer.Deserialize<DialecticsOptions>(settings.DialecticsJson, JsonOpts)
                ?? throw new InvalidOperationException(
                    $"DialecticsJson for framework '{framework}' deserialised to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"DialecticsJson for framework '{framework}' is invalid: {ex.Message}", ex);
        }

        if (result.MaxTurns <= 0 ||
            result.StabilityWindow <= 0 ||
            result.StabilityThreshold <= 0 ||
            result.MaxAscentLevels <= 0)
        {
            throw new InvalidOperationException(
                $"DialecticsJson for framework '{framework}' has non-positive required values.");
        }

        result.UserName = settings.UserName;
        cache.Set(cacheKey, result, CacheTtl);
        return result;
    }

    public void InvalidateCache(string framework)
    {
        var key = framework.ToLowerInvariant();
        cache.Remove($"qg:{key}");
        cache.Remove($"dial:{key}");
    }

    public async Task ApplyRuntimeOptionsAsync(string framework, CancellationToken ct = default)
    {
        // The agents hold references to the singleton options instances. Copying the
        // persisted values into those instances updates them in place; replacing the
        // objects would leave every already-constructed agent on the old values.
        var qualityGates = await GetQualityGateOptionsAsync(framework, ct);
        var dialectics = await GetDialecticsOptionsAsync(framework, ct);

        OptionCopier.Copy(qualityGates, defaultQualityGates);
        OptionCopier.Copy(dialectics, defaultDialectics);

        logger.LogInformation(
            "Applied persisted engine options for framework {Framework} to the live options.",
            framework);
    }
}
