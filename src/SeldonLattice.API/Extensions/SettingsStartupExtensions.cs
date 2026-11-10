using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Validation;

namespace SeldonLattice.API.Extensions;

/// <summary>
/// Makes the persisted user settings visible to the runtime at startup.
///
/// The Kernel and its provider clients are built once from configuration, so a provider
/// saved in the DB is promoted into an in-memory configuration source before the modules
/// (and therefore the Kernel) are resolved. Engine options are copied into the live
/// singleton instances in the same pass.
///
/// Provider/model changes still require a restart; engine options also apply live after a
/// settings save via <see cref="IEngineConfigService.ApplyRuntimeOptionsAsync"/>.
/// </summary>
public static class SettingsStartupExtensions
{
    public static async Task ApplyPersistedSettingsAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        var engineConfig = scope.ServiceProvider.GetRequiredService<IEngineConfigService>();

        // The application has one live runtime. The philosophy row is the primary module;
        // the research row is only a fallback when philosophy has not been seeded.
        var settings = await repo.GetAsync("philosophy") ?? await repo.GetAsync("research")
            ?? throw new InvalidOperationException(
                "No framework settings row exists. First-run seeding must create 'philosophy' before the host starts.");

        var errors = FrameworkSettingsValidator.Validate(settings);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Persisted settings for '{settings.Framework}' are incomplete: " +
                FrameworkSettingsValidator.Describe(errors));
        }

        var provider = settings.Provider;
        var values = new Dictionary<string, string?>
        {
            ["LlmProvider"] = provider,
            [$"{provider}:Model"] = settings.Model,
            [$"{provider}:Endpoint"] = settings.ApiEndpoint,
        };

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            values[$"{provider}:ApiKey"] = settings.ApiKey;

        if (app.Configuration is not ConfigurationManager manager)
        {
            throw new InvalidOperationException(
                "Configuration is not a ConfigurationManager; persisted provider settings cannot be applied.");
        }

        // Added last, so these values win over appsettings for this process.
        ((IConfigurationBuilder)manager).Add(new MemoryConfigurationSource { InitialData = values });
        app.Logger.LogInformation(
            "Applied persisted LLM provider settings for {Provider} at startup.",
            provider);

        await engineConfig.ApplyRuntimeOptionsAsync(settings.Framework);
    }
}
