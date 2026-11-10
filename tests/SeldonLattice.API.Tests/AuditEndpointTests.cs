using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using Xunit;

namespace SeldonLattice.API.Tests;

public class AuditEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuditEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("Authorization", "ApiKey dev-key-full");
    }

    [Fact]
    public async Task Get_Audit_InvalidGuid_ReturnsBadRequest400()
    {
        var response = await _client.GetAsync("/api/audit/not-a-guid");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_Audit_Unauthenticated_ReturnsUnauthorized401()
    {
        var unauthenticatedClient = _factory.CreateClient();
        var response = await unauthenticatedClient.GetAsync($"/api/audit/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Audit_ValidSession_ReturnsAuditNodesAndFilters()
    {
        // 1. Create a session
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new { RootQuestion = "Audit test question", Framework = "philosophy" });
        createResponse.EnsureSuccessStatusCode();
        var createResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = createResult.GetProperty("sessionId").GetString()!;

        // 2. Put audit nodes into store
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IMerkleStore>();

        var node1 = new LlmAuditNode
        {
            SessionId = sessionId,
            SpeakerId = "proposer",
            ModuleKey = "philosophy",
            Level = 1,
            Turn = 1,
            CompletionText = "Normal turn 1",
            Timestamp = DateTime.UtcNow.AddMinutes(-5),
            IsFlagged = false
        };

        var node2 = new LlmAuditNode
        {
            SessionId = sessionId,
            SpeakerId = "skeptic",
            ModuleKey = "philosophy",
            Level = 1,
            Turn = 2,
            CompletionText = "Flagged turn 2",
            Timestamp = DateTime.UtcNow,
            QualityFlags = ["circular_reasoning"],
            IsFlagged = true
        };

        await store.PutBlobAsync(node1);
        await store.PutBlobAsync(node2);

        // 3. Query all audit nodes
        var allResponse = await _client.GetAsync($"/api/audit/{sessionId}");
        allResponse.EnsureSuccessStatusCode();
        var allNodes = await allResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, allNodes.GetArrayLength());

        // 4. Query flagged only
        var flaggedResponse = await _client.GetAsync($"/api/audit/{sessionId}?flagged=true");
        flaggedResponse.EnsureSuccessStatusCode();
        var flaggedNodes = await flaggedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, flaggedNodes.GetArrayLength());
        Assert.Equal("skeptic", flaggedNodes[0].GetProperty("speakerId").GetString());

        // 5. Query with matching module
        var moduleResponse = await _client.GetAsync($"/api/audit/{sessionId}?module=philosophy");
        moduleResponse.EnsureSuccessStatusCode();
        var moduleNodes = await moduleResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, moduleNodes.GetArrayLength());
    }
}
