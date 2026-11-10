using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using Xunit;

namespace SeldonLattice.API.Tests;

public class IngestionCitationsEndpointTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task Get_Citations_ReturnsMockedCitations()
    {
        // Arrange: create a factory that replaces ICitationProvider with a deterministic mock
        var factory1 = factory.WithWebHostBuilder(builder =>
        {
            // Disable api-key requirement for the test and register the mock provider
            builder.ConfigureAppConfiguration((ctx, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Security:RequireApiKey"] = "false"
                });
            });

            builder.ConfigureServices(services =>
            {
                // Remove any existing registration and replace with mock
                services.RemoveAll(typeof(ICitationProvider));
                services.AddSingleton<ICitationProvider>(sp => new MockCitationProvider());
            });
        });

        var client = factory1.CreateClient();
        // Citations is a `reader` route (granular roles, api-hardening-2026-10-20);
        // the X-Api-Key belt-and-braces check stays disabled via RequireApiKey=false.
        client.DefaultRequestHeaders.Add("Authorization", "ApiKey dev-key-reader");

        // Act
        var response = await client.GetAsync("/api/ingestion/citations?q=test&topN=2");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<Citation>>();
        Assert.NotNull(list);
        Assert.Equal(2, list!.Count);
        Assert.All(list, c => Assert.False(string.IsNullOrEmpty(c.Title)));
    }

    private class MockCitationProvider : ICitationProvider
    {
        public Task<IReadOnlyList<Citation>> GetTopCitationsAsync(string query, int topN = 5, CancellationToken ct = default)
        {
            var results = new List<Citation>();
            for (int i = 0; i < topN; i++)
            {
                results.Add(new Citation
                {
                    Id = $"mock-{i}",
                    Title = $"Mock Source {i} for '{query}'",
                    Url = $"https://example.org/mock/{i}",
                    SourceId = "mock-source",
                    Confidence = 0.9 - i * 0.1
                });
            }

            return Task.FromResult((IReadOnlyList<Citation>)results);
        }
    }
}
