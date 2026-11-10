using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using Xunit;

namespace SeldonLattice.API.Tests;

/// <summary>
/// The per-turn quality metrics are persisted on the node and must reach the wire on
/// every surface the frontend reconstructs from. A value can be produced and stored
/// correctly and still never appear because the API projection omits it, so these
/// assert on the HTTP response rather than on the store.
/// </summary>
public class NodeMetricsEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public NodeMetricsEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
        // Session create (writer) + nodes/history reads (reader): dev-key-full
        // carries both (granular roles, api-hardening-2026-10-20).
        _client.DefaultRequestHeaders.Add("Authorization", "ApiKey dev-key-full");
    }

    private async Task<string> CreateSessionAsync()
    {
        var res = await _client.PostAsJsonAsync("/api/sessions", new { RootQuestion = "Metrics projection test", Framework = "philosophy" });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("sessionId").GetString()!;
    }

    [Fact]
    public async Task Get_Nodes_Returns_persisted_quality_metrics()
    {
        var sessionId = await CreateSessionAsync();

        var store = _factory.Services.GetRequiredService<IMerkleStore>();
        await store.PutBlobAsync(new DialecticalNode
        {
            SessionId = sessionId,
            NodeType = "skeptic_reply",
            Level = 0,
            SpeakerId = "skeptic",
            Content = "skeptic responds",
            Timestamp = DateTime.UtcNow,
            Turn = 0,
            QualityScore = 78.5,
            CeremonyScore = 1.25,
            SimilarityToPrevious = 0.31,
            NewArgumentCount = 4,
            WordCount = 210,
            HasFarewell = false,
            HasAgreement = true,
            HasDisagreement = true,
            SelfSimilarity = 0.12,
        });

        var response = await _client.GetAsync($"/api/nodes?sessionId={sessionId}");
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();

        var node = page.GetProperty("items").EnumerateArray()
            .Single(n => n.GetProperty("nodeType").GetString() == "skeptic_reply");

        Assert.Equal(78.5, node.GetProperty("qualityScore").GetDouble());
        Assert.Equal(1.25, node.GetProperty("ceremonyScore").GetDouble());
        Assert.Equal(0.31, node.GetProperty("similarityToPrevious").GetDouble());
        Assert.Equal(4, node.GetProperty("newArgumentCount").GetInt32());
        Assert.Equal(210, node.GetProperty("wordCount").GetInt32());
        Assert.False(node.GetProperty("hasFarewell").GetBoolean());
        Assert.True(node.GetProperty("hasAgreement").GetBoolean());
        Assert.True(node.GetProperty("hasDisagreement").GetBoolean());
        Assert.Equal(0.12, node.GetProperty("selfSimilarity").GetDouble());
    }

    [Fact]
    public async Task Get_Nodes_Reports_null_for_an_unscored_node()
    {
        // A question node never passes through the quality gates. The wire must say
        // "not measured" rather than inventing a zero, or analytics cannot separate the
        // two - which is the whole reason these fields are nullable.
        var sessionId = await CreateSessionAsync();

        var store = _factory.Services.GetRequiredService<IMerkleStore>();
        await store.PutBlobAsync(new DialecticalNode
        {
            SessionId = sessionId,
            NodeType = "question",
            Level = 0,
            Content = "What is philosophy?",
            Timestamp = DateTime.UtcNow,
        });

        var response = await _client.GetAsync($"/api/nodes?sessionId={sessionId}");
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();

        var node = page.GetProperty("items").EnumerateArray()
            .Single(n => n.GetProperty("nodeType").GetString() == "question");

        Assert.Equal(JsonValueKind.Null, node.GetProperty("qualityScore").ValueKind);
        Assert.Equal(JsonValueKind.Null, node.GetProperty("wordCount").ValueKind);
    }

    [Fact]
    public async Task Get_SessionHistory_Returns_quality_score_not_variability()
    {
        // History used to report VariabilityScore under the name qualityScore - a
        // different quantity (semantic movement between turns). It now reports the gate
        // score, and null where the gates did not run.
        var sessionId = await CreateSessionAsync();

        var store = _factory.Services.GetRequiredService<IMerkleStore>();
        await store.PutBlobAsync(new DialecticalNode
        {
            SessionId = sessionId,
            NodeType = "skeptic_reply",
            Level = 0,
            SpeakerId = "skeptic",
            Content = "skeptic responds",
            Timestamp = DateTime.UtcNow,
            QualityScore = 64.0,
            // Deliberately different, so aliasing one for the other fails the assert.
            VariabilityScore = 0.987f,
        });

        var response = await _client.GetAsync($"/api/sessions/{sessionId}/history");
        response.EnsureSuccessStatusCode();
        var history = await response.Content.ReadFromJsonAsync<JsonElement>();

        var turn = history.GetProperty("turns").EnumerateArray()
            .Single(t => t.GetProperty("speaker").GetString() == "skeptic");

        Assert.Equal(64.0, turn.GetProperty("qualityScore").GetDouble());
    }
}
