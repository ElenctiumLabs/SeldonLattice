using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Infrastructure.Extensions;
using SeldonLattice.Infrastructure.Storage;
using Xunit;

namespace SeldonLattice.Application.Tests;

public class CitationProviderRegistrationTests
{
    [Fact]
    public void CitationProvider_IsRegisteredInServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IMerkleStore>(sp =>
            new FasterMerkleStore(Path.Combine(Path.GetTempPath(), "seldon-test", Guid.NewGuid().ToString())));
        services.AddSingleton<Kernel>(Kernel.CreateBuilder().Build());
        services.AddScoped<ISessionService, Infrastructure.Sessions.SessionService>();
        services.AddDialecticsCore(new ConfigurationBuilder().Build());
        services.AddPhilosophyModule(new ConfigurationBuilder().Build());
        services.AddSingleton<IProviderRateLimiter, NoOpProviderRateLimiter>();

        // Before building the provider, ensure the service collection contains a descriptor for ICitationProvider
        var hasDescriptor = services.Any(d => d.ServiceType == typeof(ICitationProvider));
        Assert.True(hasDescriptor, "ICitationProvider should be registered in the service collection by AddDialecticsCore()");
    }
}