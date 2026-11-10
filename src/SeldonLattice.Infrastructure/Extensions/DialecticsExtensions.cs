using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SeldonLattice.Application.Services;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Options;
using SeldonLattice.Infrastructure.Modules;
using SeldonLattice.Infrastructure.Services;

namespace SeldonLattice.Infrastructure.Extensions;

/// <summary>
/// Shared dialectics infrastructure: options, module registry, and cross-module services.
/// Each module registers its own agents and services
/// via separate extension methods.
/// </summary>
public static class DialecticsExtensions
{
    /// <summary>
    /// Registers shared dialectics infrastructure: DialecticsOptions from the Dialectics configuration section,
    /// QualityGateOptions, CyclePhaseDetector, DialogueQualityGates, and the DialecticsModuleRegistry singleton.
    /// </summary>
    public static IServiceCollection AddDialecticsCore(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        // Base dialectics options
        var dialecticsOptions = configuration.GetSection("Dialectics");
        services.Configure<DialecticsOptions>(dialecticsOptions.Bind);
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DialecticsOptions>>().Value);

        // Shared engine services used by all modules
        services.Configure<QualityGateOptions>(configuration.GetSection("QualityGates"));
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<QualityGateOptions>>().Value);
        services.AddScoped<CyclePhaseDetector>();
        services.AddScoped<DialogueQualityGates>();
        services.AddSingleton<ILlmUsageCollector, LlmUsageCollector>();

        // Citation retrieval from ingested documents (framework-agnostic capability kept
        // when the domain modules were retired; the ascent engine optionally consumes it).
        services.AddScoped<ICitationProvider, MerkleCitationProvider>();

        // Module registry (singleton, shared across all modules)
        services.AddSingleton<IDialecticsModuleRegistry, DialecticsModuleRegistry>();

        return services;
    }

    /// <summary>
    /// Scans the DI container for all IDialecticsModule implementations and registers them
    /// in the module registry at application startup.
    /// </summary>
    public static void RegisterAllModules(
        this IServiceProvider serviceProvider,
        IDialecticsModuleRegistry registry
    )
    {
        var modules = serviceProvider.GetServices<IDialecticsModule>();
        foreach (var module in modules)
        {
            registry.RegisterModule(
                module.Key,
                module,
                scopedResolver: sp =>
                    sp.GetServices<IDialecticsModule>().FirstOrDefault(m => m.Key == module.Key) ?? module);
        }
    }
}
