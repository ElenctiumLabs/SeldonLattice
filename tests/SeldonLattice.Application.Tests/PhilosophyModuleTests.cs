using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Infrastructure.Extensions;
using SeldonLattice.Infrastructure.Storage;
using Xunit;

namespace SeldonLattice.Application.Tests;

public class PhilosophyModuleTests
{
    [Fact]
    public void PhilosophyModule_CanBeRegisteredAndRetrieved()
    {
        var services = new ServiceCollection();
        var dataDir = Path.Combine(Path.GetTempPath(), "seldon-test", Guid.NewGuid().ToString());
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IMerkleStore>(sp =>
            new FasterMerkleStore(Path.Combine(dataDir, "merkle")));
        services.AddSingleton<Kernel>(Kernel.CreateBuilder().Build());
        services.AddDialecticsCore(new ConfigurationBuilder().Build());
        TestSessionSupport.AddTestSessionService(services, dataDir);
        services.AddPhilosophyModule(new ConfigurationBuilder().Build());
        services.AddSingleton<IProviderRateLimiter, NoOpProviderRateLimiter>();

        var provider = services.BuildServiceProvider();
        TestSessionSupport.EnsureSessionsCreated(provider);
        var registry = provider.GetRequiredService<IDialecticsModuleRegistry>();
        provider.RegisterAllModules(registry);

        var module = registry.GetModule("philosophy");
        Assert.NotNull(module);
        Assert.Equal("philosophy", module.Key);
        Assert.Equal("Philosophical Reasoning", module.DisplayName);
    }

    [Fact]
    public void PhilosophyModule_IsRegisteredInRegistry()
    {
        var services = new ServiceCollection();
        var dataDir = Path.Combine(Path.GetTempPath(), "seldon-test", Guid.NewGuid().ToString());
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IMerkleStore>(sp =>
            new FasterMerkleStore(Path.Combine(dataDir, "merkle")));
        services.AddSingleton<Kernel>(Kernel.CreateBuilder().Build());
        services.AddDialecticsCore(new ConfigurationBuilder().Build());
        TestSessionSupport.AddTestSessionService(services, dataDir);
        services.AddPhilosophyModule(new ConfigurationBuilder().Build());
        services.AddSingleton<IProviderRateLimiter, NoOpProviderRateLimiter>();

        var provider = services.BuildServiceProvider();
        TestSessionSupport.EnsureSessionsCreated(provider);
        var registry = provider.GetRequiredService<IDialecticsModuleRegistry>();
        provider.RegisterAllModules(registry);

        var modules = registry.GetAvailableModules().ToList();
        Assert.Contains("philosophy", modules);
    }
}
