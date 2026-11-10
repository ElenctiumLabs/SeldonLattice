using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Infrastructure.Modules;

namespace SeldonLattice.Infrastructure.Extensions;

/// <summary>
/// DI registrations for the Academic Research module.
/// </summary>
public static class ResearchModuleExtensions
{
    /// <summary>
    /// Registers the Research module. This module orchestrates the philosophy module and any runtime custom
    /// modules via the IDialecticsModuleRegistry - no new agents or services are needed.
    /// </summary>
    public static IServiceCollection AddResearchModule(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddScoped<IDialecticsModule, ResearchModule>();

        return services;
    }
}
