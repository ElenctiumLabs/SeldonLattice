using Microsoft.Extensions.Options;
using SeldonLattice.API.Middleware;
using SeldonLattice.API.Services;
using SeldonLattice.Application.Memory;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Options;
using SeldonLattice.Infrastructure.Extensions;
using SeldonLattice.Infrastructure.Services;
using SeldonLattice.Infrastructure.Settings;
using SeldonLattice.Infrastructure.Verification;

namespace SeldonLattice.API.Extensions;

/// <summary>
/// The dialectical reasoning engine's shared services, cross-session claim memory, session
/// integrity verification, engine configuration, and dialectics modules.
/// </summary>
public static class DialecticsServiceExtensions
{
    public static IServiceCollection AddSeldonLatticeDialectics(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddTransient<IChatService, ChatService>();

        //  Dialectics Core (shared options, registry, quality gates) 
        services.Configure<DefaultsOptions>(configuration.GetSection("Defaults"));
        services.Configure<AscendOptions>(configuration.GetSection("Ascend"));
        services.Configure<RateLimitOptions>(configuration.GetSection("RateLimiting"));
        services.Configure<LlmOptions>(configuration.GetSection("HttpClient:Llm"));
        services.Configure<SeedDataOptions>(configuration.GetSection("SeedData"));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<LlmOptions>>().Value);
        services.Configure<ProviderRateLimitOptions>(configuration.GetSection("ProviderRateLimits"));
        services.AddSingleton<IProviderRateLimiter, ProviderRateLimiter>();

        //  Cross-session claim memory (root-anchored RAG over ArgumentClaimNode) 
        services.AddScoped<IClaimMemoryRetriever, ClaimMemoryRetriever>();
        services.AddDialecticsCore(configuration);

        //  Session integrity verification (row 5: Merkle CIDs, references, ML-DSA signatures) 
        services.AddSingleton<ISessionVerifier, SessionVerifier>();
        services.AddSingleton<ExportVerifier>();

        //  Engine Config Service (DB-backed, replaces static IOptions for QG + Dialectics reads) 
        //  Per-framework option literals died with their retired modules.
        services.AddMemoryCache();
        services.AddSingleton<ServerLaunchService>();
        services.AddScoped<IEngineConfigService>(sp => new EngineConfigService(
            sp.GetRequiredService<ISettingsRepository>(),
            sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
            sp.GetRequiredService<ILogger<EngineConfigService>>(),
            sp.GetRequiredService<QualityGateOptions>(),
            sp.GetRequiredService<DialecticsOptions>()));

        //  Module Registrations 
        services.AddPhilosophyModule(configuration);
        // The base type alias is used by DynamicDialecticsModule (runtime-created custom modules)
        // which needs a generic ascent service engine. Philosophy is the reference implementation.
        services.AddScoped<DialecticalAscentServiceBase>(sp =>
            sp.GetRequiredService<PhilosophyDialecticalAscentService>());
        services.AddResearchModule(configuration);

        return services;
    }
}
