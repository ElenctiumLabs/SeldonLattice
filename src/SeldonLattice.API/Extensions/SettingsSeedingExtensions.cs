using Microsoft.Extensions.Options;
using SeldonLattice.API.Services;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Settings;
using SeldonLattice.Domain.Options;
using SeldonLattice.Domain.Validation;

namespace SeldonLattice.API.Extensions;

/// <summary>
/// First-run seeding only. If a framework row does not exist, build it from deployment
/// defaults (appsettings plus the bound engine options) and persist it. If it exists, do
/// nothing: the DB is the runtime source of truth and an incomplete row is reported by the
/// settings API/runtime, not silently repaired.
/// </summary>
public static class SettingsSeedingExtensions
{
    private static readonly string[] Frameworks = ["philosophy", "research"];

    public static async Task SeedDefaultFrameworkSettingsAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var qualityGates = scope.ServiceProvider.GetRequiredService<IOptions<QualityGateOptions>>().Value;
        var dialectics = scope.ServiceProvider.GetRequiredService<IOptions<DialecticsOptions>>().Value;

        foreach (var framework in Frameworks)
        {
            var existing = await repo.GetAsync(framework);
            if (existing is not null)
            {
                // Older builds forced IsConfigured=true while seeding and wrote a
                // hardcoded OpenAI/OpenCodeZen fingerprint. That made the old default row
                // look like a user save and shadow appsettings. Refresh only that exact
                // fingerprint; any other existing row is user-owned and untouched.
                if (IsLegacySeedFingerprint(existing))
                {
                    var fresh = SettingsDefaults.Create(framework, config, qualityGates, dialectics);
                    existing.Provider = fresh.Provider;
                    existing.Model = fresh.Model;
                    existing.ApiEndpoint = fresh.ApiEndpoint;
                    existing.ApiKey = fresh.ApiKey;
                    existing.QualityGatesJson = fresh.QualityGatesJson;
                    existing.DialecticsJson = fresh.DialecticsJson;
                    existing.IsConfigured = false;

                    await repo.UpsertAsync(existing);
                    app.Logger.LogWarning(
                        "[Seed] Migrated legacy seeded settings for '{Framework}' to the deployment provider defaults. " +
                        "The framework will be shown in the settings gate for confirmation.",
                        framework);
                }

                continue;
            }

            var seeded = SettingsDefaults.Create(framework, config, qualityGates, dialectics);
            var errors = FrameworkSettingsValidator.Validate(seeded);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Cannot seed settings for framework '{framework}'. Deployment defaults are incomplete: " +
                    FrameworkSettingsValidator.Describe(errors));
            }

            await repo.UpsertAsync(seeded);
            app.Logger.LogInformation(
                "[Seed] Framework settings and engine config seeded for '{Framework}'",
                framework);
        }
    }

    /// <summary>
    /// Identifies settings that still point at the retired local Bionic endpoint. Refreshing
    /// those rows moves local inference to the current deployment defaults; no request is sent
    /// to the old endpoint. The /api branch is limited to the exact old seed models.
    /// </summary>
    private static bool IsLegacySeedFingerprint(FrameworkSettings settings) =>
        settings.ApiEndpoint == "http://localhost:1235/v1"
        || ((settings.Provider is "OpenAI" or "OpenCodeZen")
            && (settings.Model is "mistral-7b-instruct-v0.3" or "deepseek-v4-flash-free" or "qwen2.5-14b-instruct")
            && settings.ApiEndpoint == "/api");
}
