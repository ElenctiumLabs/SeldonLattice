using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SeldonLattice.Application.Agents.Dialecticians.Philosophy;
using SeldonLattice.Application.Agents.Moderators;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Options;
using SeldonLattice.Infrastructure.Modules;
using SeldonLattice.Infrastructure.Services;

namespace SeldonLattice.Infrastructure.Extensions;

/// <summary>
/// DI registrations for the Philosophy module (proposer/skeptic with mediator).
/// </summary>
public static class PhilosophyModuleExtensions
{
    /// <summary>
    /// Registers the Philosophy module services. Binds PhilosophyDialecticsOptions
    /// from the Dialectics configuration section.
    /// </summary>
    public static void AddPhilosophyModule(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind base Dialectics first, then overlay Philosophy sub-section (if present)
        services.Configure<PhilosophyDialecticsOptions>(configuration.GetSection("Dialectics"));
        services.Configure<PhilosophyDialecticsOptions>(configuration.GetSection("Dialectics:Philosophy"));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<PhilosophyDialecticsOptions>>().Value);

        // Agents (role-keyed, faculty-agnostic)
        services.AddScoped<ProposerAgent>();
        services.AddScoped<SkepticAgent>();
        // MediatorAgent receives the debating agent display names so its prompts are
        // accurate rather than relying on hardcoded defaults.
        services.AddScoped<MediatorAgent>(sp => new MediatorAgent(
            sp.GetRequiredService<Microsoft.SemanticKernel.Kernel>(),
            sp.GetRequiredService<IOptions<DialecticsOptions>>(),
            sp.GetRequiredService<IProviderRateLimiter>(),
            dialectician1Name: "Proposer",
            dialectician2Name: "Skeptic",
            dialogueLabel: "philosophical"));

        // Ascent service
        services.AddScoped<PhilosophyDialecticalAscentService>();

        // Module
        services.AddScoped<IDialecticsModule, PhilosophyModule>();
    }
}
