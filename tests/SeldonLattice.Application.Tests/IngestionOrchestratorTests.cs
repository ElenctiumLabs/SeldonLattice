using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Infrastructure.Ingestion;
using SeldonLattice.Infrastructure.Storage;
using Xunit;

namespace SeldonLattice.Application.Tests;

public class IngestionOrchestratorTests
{
    private static ServiceCollection BuildServices(string dataDir)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataDir"] = dataDir,
                ["Defaults:IngestionMaxResults"] = "10",
                ["Ingestion:BiasThreshold"] = "0.5",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IMerkleStore>(sp =>
            new FasterMerkleStore(Path.Combine(dataDir, "merkle")));
        services.AddSingleton<Kernel>(Kernel.CreateBuilder().Build());
        services.AddSingleton<DocumentNormalizer>();
        services.AddSingleton<AuditEngine>();
        services.AddSingleton<IIngestionOrchestrator, IngestionOrchestrator>();
        return services;
    }

    [Fact]
    public async Task RunIngestionCycleAsync_ReturnsResultWithSourceResults()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "seldon-test", Guid.NewGuid().ToString());
        var services = BuildServices(dataDir);
        var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<IIngestionOrchestrator>();

        // Seed skewed documents so the audit reports bias above threshold and the
        // orchestrator produces a falsification result (matching current behavior).
        var merkleStore = provider.GetRequiredService<IMerkleStore>();
        for (int i = 0; i < 10; i++)
        {
            await merkleStore.PutBlobAsync(new IngestedDocument
            {
                ExternalId = $"skewed-doc-{i}",
                SourceId = "western-source",
                Culture = "western",
                Domain = "philosophy",
                Body = $"Skewed document {i}",
                Embedding = [0.1f, 0.2f, 0.3f],
            });
        }

        var result = await orchestrator.RunIngestionCycleAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result.SourceResults);
        Assert.NotNull(result.BiasReport);
        Assert.NotNull(result.FalsificationResult);
    }

    [Fact]
    public async Task RunIngestionCycleAsync_WithNoConnectors_ReturnsEmptyResults()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "seldon-test", Guid.NewGuid().ToString());
        var services = BuildServices(dataDir);
        var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<IIngestionOrchestrator>();

        var result = await orchestrator.RunIngestionCycleAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Empty(result.SourceResults);
        Assert.Equal(0, result.TotalNewDocuments);
        Assert.Equal(0, result.TotalErrors);
    }
}