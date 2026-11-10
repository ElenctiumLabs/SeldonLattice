using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Infrastructure.Ingestion;
using SeldonLattice.Infrastructure.Storage;
using Xunit;

namespace SeldonLattice.Application.Tests;

public class AuditEngineTests
{
    private readonly AuditEngine _auditEngine;
    private readonly IMerkleStore _merkleStore;

    public AuditEngineTests()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "seldon-test", Guid.NewGuid().ToString());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataDir"] = dataDir,
                ["Ingestion:BiasThreshold"] = "0.5",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IConfiguration>(configuration);
        _merkleStore = new FasterMerkleStore(Path.Combine(dataDir, "merkle"));
        services.AddSingleton<IMerkleStore>(_merkleStore);
        services.AddSingleton<AuditEngine>();

        var provider = services.BuildServiceProvider();
        _auditEngine = provider.GetRequiredService<AuditEngine>();
    }

    [Fact]
    public async Task AuditAsync_WithNoDocuments_ReturnsZeroBiasScore()
    {
        var report = await _auditEngine.AuditAsync(CancellationToken.None);

        Assert.NotNull(report);
        Assert.Equal(0f, report.BiasScore);
        // The audit always reports the full known culture/domain spectrum - with
        // no documents every bucket is zero rather than absent.
        Assert.Equal(7, report.CultureDistribution.Count);
        Assert.All(report.CultureDistribution.Values, v => Assert.Equal(0, v));
        Assert.All(report.DomainDistribution.Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public async Task AuditAsync_WithBalancedDocuments_ReturnsLowBiasScore()
    {
        await SeedDocumentsAsync();

        var report = await _auditEngine.AuditAsync(CancellationToken.None);

        Assert.NotNull(report);
        // Balanced across cultures AND domains - no single group dominates, so the
        // weighted bias score stays at/below the 0.5 threshold.
        Assert.InRange(report.BiasScore, 0f, 0.5f);
    }

    [Fact]
    public async Task AuditAsync_WithSkewedDocuments_ReturnsHighBiasScore()
    {
        await SeedSkewedDocumentsAsync();

        var report = await _auditEngine.AuditAsync(CancellationToken.None);

        Assert.NotNull(report);
        Assert.True(report.BiasScore > 0.5f);
    }

    [Fact]
    public async Task AuditAsync_ReportContainsRecommendedSources()
    {
        await SeedSkewedDocumentsAsync();

        var report = await _auditEngine.AuditAsync(CancellationToken.None);

        Assert.NotNull(report.RecommendedSources);
        Assert.NotEmpty(report.RecommendedSources);
    }

    [Fact]
    public async Task AuditAsync_ReportContainsSuggestedThemes()
    {
        await SeedSkewedDocumentsAsync();

        var report = await _auditEngine.AuditAsync(CancellationToken.None);

        Assert.NotNull(report.SuggestedFalsificationThemes);
    }

    private async Task SeedDocumentsAsync()
    {
        // 2x2 matrix: two cultures × two domains, one document each, so neither
        // culture nor domain dominates the audit distribution.
        var docs = new[]
        {
            new IngestedDocument { ExternalId = "doc-1", SourceId = "test-source", Culture = "western", Domain = "philosophy", Body = "Test document", Embedding = [0.1f, 0.2f, 0.3f] },
            new IngestedDocument { ExternalId = "doc-2", SourceId = "test-source-2", Culture = "eastern", Domain = "philosophy", Body = "Test document 2", Embedding = [0.2f, 0.3f, 0.4f] },
            new IngestedDocument { ExternalId = "doc-3", SourceId = "test-source-3", Culture = "western", Domain = "economics", Body = "Test document 3", Embedding = [0.3f, 0.4f, 0.5f] },
            new IngestedDocument { ExternalId = "doc-4", SourceId = "test-source-4", Culture = "eastern", Domain = "economics", Body = "Test document 4", Embedding = [0.4f, 0.5f, 0.6f] },
        };
        foreach (var doc in docs)
            await _merkleStore.PutBlobAsync(doc);
    }

    private async Task SeedSkewedDocumentsAsync()
    {
        for (int i = 0; i < 10; i++)
        {
            var doc = new IngestedDocument
            {
                ExternalId = $"skewed-doc-{i}",
                SourceId = "western-source",
                Culture = "western",
                Domain = "philosophy",
                Body = $"Skewed document {i}",
                Embedding = [0.1f, 0.2f, 0.3f],
            };
            await _merkleStore.PutBlobAsync(doc);
        }
    }
}