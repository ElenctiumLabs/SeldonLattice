using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using Xunit;

namespace SeldonLattice.API.Tests;

public class SessionEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SessionEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
        // Sessions are user-scoped (CurrentUserService resolves the owning user) and
        // granular: reads require the `reader` role, mutations require `writer`.
        // dev-key-full carries both — docs/audits/api-hardening-2026-10-20.md.
        _client.DefaultRequestHeaders.Add("Authorization", "ApiKey dev-key-full");
    }

    [Fact]
    public async Task Post_Sessions_CreatesSessionAndReturnsSessionId()
    {
        var response = await _client.PostAsJsonAsync("/api/sessions", new { RootQuestion = "What is justice?", Framework = "philosophy" });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.TryGetProperty("sessionId", out var sessionId));
        Assert.False(string.IsNullOrEmpty(sessionId.GetString()));
    }

    [Fact]
    public async Task Get_Sessions_ReturnsListOfSessions()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new { RootQuestion = "Test session", Framework = "philosophy" });
        createResponse.EnsureSuccessStatusCode();

        var listResponse = await _client.GetAsync("/api/sessions");
        listResponse.EnsureSuccessStatusCode();

        JsonElement? sessions = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotNull(sessions);
    }

    [Fact]
    public async Task Get_SessionById_ReturnsSession()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new { RootQuestion = "Get session test", Framework = "philosophy" });
        createResponse.EnsureSuccessStatusCode();
        var createResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = createResult.GetProperty("sessionId").GetString()!;

        var response = await _client.GetAsync($"/api/sessions/{sessionId}");
        response.EnsureSuccessStatusCode();

        JsonElement? session = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotNull(session);
        Assert.Equal("Get session test", session.Value.GetProperty("rootQuestion").GetString());
    }

    [Fact]
    public async Task Get_SessionById_NonExistent_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/sessions/non-existent-id");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_SessionById_ReturnsOk()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new { RootQuestion = "Delete session test", Framework = "philosophy" });
        createResponse.EnsureSuccessStatusCode();
        var createResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = createResult.GetProperty("sessionId").GetString()!;

        var deleteResponse = await _client.DeleteAsync($"/api/sessions/{sessionId}");
        deleteResponse.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Get_SessionHistory_ReturnsDebateChain()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new { RootQuestion = "History test", Framework = "philosophy" });
        createResponse.EnsureSuccessStatusCode();
        var createResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = createResult.GetProperty("sessionId").GetString()!;

        var response = await _client.GetAsync($"/api/sessions/{sessionId}/history");
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Get_SessionReplay_ReturnsReplayFormat()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new { RootQuestion = "Replay test", Framework = "philosophy" });
        createResponse.EnsureSuccessStatusCode();
        var createResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = createResult.GetProperty("sessionId").GetString()!;

        var response = await _client.GetAsync($"/api/sessions/{sessionId}/replay");
        Assert.True(response.IsSuccessStatusCode);

        JsonElement? replay = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotNull(replay);
        Assert.Equal(sessionId, replay.Value.GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task Get_SessionClaims_ReturnsSeededClaimsScopedToSession()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new { RootQuestion = "Claims test", Framework = "philosophy" });
        createResponse.EnsureSuccessStatusCode();
        var createResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = createResult.GetProperty("sessionId").GetString()!;

        // Seed two claims through the app's real signed Merkle store, exactly as the
        // ascent service would persist them.
        var store = _factory.Services.GetRequiredService<IMerkleStore>();
        await store.PutBlobAsync(new ArgumentClaimNode
        {
            SessionId = sessionId,
            Level = 1,
            ClaimText = "A tiered basic income is a single verifiable proposition",
            Confidence = 0.85f,
            FalsifiedBy = "#fails-to-pass employment data",
            SupportedBy = ["synthesis-cid-1"],
            ParentHashes = ["synthesis-cid-1"],
        });
        await store.PutBlobAsync(new ArgumentClaimNode
        {
            SessionId = sessionId,
            Level = 2,
            ClaimText = "Universal coverage requires a different fiscal instrument",
            Confidence = 0.7f,
            FalsifiedBy = "#cost-model contradicts it",
            ParentHashes = ["synthesis-cid-2"],
        });
        await store.PutBlobAsync(new ArgumentClaimNode
        {
            SessionId = sessionId + "-other",
            Level = 9,
            ClaimText = "This claim belongs to a different session",
            Confidence = 0.1f,
            ParentHashes = ["other-cid"],
        });

        var response = await _client.GetAsync($"/api/sessions/{sessionId}/claims");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(sessionId, body.GetProperty("sessionId").GetString());
        Assert.Equal(2, body.GetProperty("totalClaims").GetInt32());

        var claims = body.GetProperty("claims");
        Assert.Equal("A tiered basic income is a single verifiable proposition",
            claims[0].GetProperty("claim").GetString());
        Assert.Equal(0.85, claims[0].GetProperty("confidence").GetDouble(), 3);
        Assert.Equal(1, claims[0].GetProperty("level").GetInt32());
        Assert.Equal("synthesis-cid-1", claims[0].GetProperty("synthesisHash").GetString());
        // Ordered by level: the level-2 claim comes second.
        Assert.Equal(2, claims[1].GetProperty("level").GetInt32());
    }

    [Fact]
    public async Task Get_SessionClaims_NonExistentSession_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/sessions/session-that-does-not-exist/claims");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HistoryAndReplay_SurfaceAscentScore()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new { RootQuestion = "Ascent score surfacing", Framework = "philosophy" });
        createResponse.EnsureSuccessStatusCode();
        var createResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = createResult.GetProperty("sessionId").GetString()!;

        var store = _factory.Services.GetRequiredService<IMerkleStore>();
        await store.PutBlobAsync(new DialecticalNode
        {
            NodeType = "synthesis",
            SpeakerId = "socrates",
            Level = 1,
            Content = "Synthesis node with an Ascent-quality score",
            Embedding = [0.1f, 0.2f],
            Timestamp = new DateTime(2026, 9, 13, 13, 0, 0, DateTimeKind.Utc),
            SessionId = sessionId,
            AscentScore = 0.84f,
        });

        var history = await (await _client.GetAsync($"/api/sessions/{sessionId}/history")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0.84, history.GetProperty("turns")[0].GetProperty("ascentScore").GetDouble(), 3);

        var replay = await (await _client.GetAsync($"/api/sessions/{sessionId}/replay")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0.84, replay.GetProperty("turns")[0].GetProperty("ascentScore").GetDouble(), 3);
    }

    [Fact]
    public async Task Get_ClaimMemory_RecallsOtherSessionsClaims()
    {
        var createOwner = await _client.PostAsJsonAsync("/api/sessions", new { RootQuestion = "Claim memory owner", Framework = "philosophy" });
        createOwner.EnsureSuccessStatusCode();
        var ownerId = (await createOwner.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetString()!;

        var store = _factory.Services.GetRequiredService<IMerkleStore>();
        await store.PutBlobAsync(new ArgumentClaimNode
        {
            SessionId = "prior-session", Level = 1,
            ClaimText = "Universal basic income needs a dedicated revenue instrument",
            Confidence = 0.8f,
            FalsifiedBy = "#cost model contradicts it",
            SupportedBy = ["prior-synth"], ParentHashes = ["prior-synth"],
        });
        await store.PutBlobAsync(new ArgumentClaimNode
        {
            SessionId = ownerId, Level = 1,
            ClaimText = "basic income boundary for today's discussion",
            Confidence = 0.9f,
            SupportedBy = ["mine"], ParentHashes = ["mine"],
        });

        var response = await _client.GetAsync($"/api/claims/memory?query=basic%20income&excludeSession={ownerId}");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var recalled = body.GetProperty("recalled");
        // The shared per-factory Merkle store is additive across tests, so don't assert a
        // global count - assert the explicitly seeded prior-session claim was recalled and
        // that the owning session's own claim was excluded.
        Assert.True(recalled.GetArrayLength() >= 1);
        var priorClaim = recalled.EnumerateArray().First(r => r.GetProperty("sessionId").GetString() == "prior-session");
        Assert.Contains("revenue instrument", priorClaim.GetProperty("claim").GetString());
        Assert.All(
            recalled.EnumerateArray(),
            r => Assert.NotEqual(ownerId, r.GetProperty("sessionId").GetString()));
    }

    [Fact]
    public async Task Get_ClaimMemory_MissingQuery_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/claims/memory");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_Health_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/api/health");
        response.EnsureSuccessStatusCode();

        JsonElement? health = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotNull(health);
    }

    [Fact]
    public async Task Get_DialecticsConfig_ReturnsConfiguration()
    {
        var response = await _client.GetAsync("/api/dialectics/config?framework=philosophy");
        response.EnsureSuccessStatusCode();

        JsonElement? config = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotNull(config);
    }

    [Fact]
    public async Task Get_Nodes_ReturnsNodesForSession()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new { RootQuestion = "Nodes test", Framework = "philosophy" });
        createResponse.EnsureSuccessStatusCode();
        var createResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = createResult.GetProperty("sessionId").GetString()!;

        var response = await _client.GetAsync($"/api/nodes?sessionId={sessionId}");
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Get_ExportJson_IncludesVerifyBlock()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new { RootQuestion = "Verify block test", Framework = "philosophy" });
        createResponse.EnsureSuccessStatusCode();
        var createResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = createResult.GetProperty("sessionId").GetString()!;

        // Seed a signed question node for the session so the verifier has a chain to audit.
        var store = _factory.Services.GetRequiredService<IMerkleStore>();
        await store.PutBlobAsync(new DialecticalNode
        {
            SessionId = sessionId,
            NodeType = "question",
            Level = 0,
            SpeakerId = "moderator",
            Content = "Verify block test",
            Timestamp = DateTime.UtcNow,
        });

        var response = await _client.GetAsync($"/api/sessions/{sessionId}/export?format=json");
        response.EnsureSuccessStatusCode();

        var export = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(export.TryGetProperty("sessionId", out var rootSid) && rootSid.GetString() == sessionId, "JSON export must carry root `sessionId`");
        Assert.True(export.TryGetProperty("session", out var sessionObj) && sessionObj.TryGetProperty("sessionId", out var nestedSid) && nestedSid.GetString() == sessionId, "JSON export must carry `session.sessionId`");
        Assert.True(export.TryGetProperty("verify", out var verify), "JSON export must carry a `verify` block");
        Assert.True(verify.TryGetProperty("verified", out var verified));
        Assert.True(verify.TryGetProperty("nodesChecked", out var nodesChecked));
        Assert.True(nodesChecked.GetInt32() == 1, "Seeded question node must be audited");
        Assert.True(verify.TryGetProperty("signaturesVerified", out _));
        Assert.True(verify.TryGetProperty("issues", out _));
    }

    [Fact]
    public async Task Get_SessionVerify_ReturnsVerificationReport()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new { RootQuestion = "Verify endpoint test", Framework = "philosophy" });
        createResponse.EnsureSuccessStatusCode();
        var createResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = createResult.GetProperty("sessionId").GetString()!;

        // Seed a signed node so the verifier audits a real member of the session.
        var store = _factory.Services.GetRequiredService<IMerkleStore>();
        await store.PutBlobAsync(new DialecticalNode
        {
            SessionId = sessionId,
            NodeType = "question",
            Level = 0,
            SpeakerId = "moderator",
            Content = "Verify endpoint test",
            Timestamp = DateTime.UtcNow,
        });

        var response = await _client.GetAsync($"/api/sessions/{sessionId}/verify");
        response.EnsureSuccessStatusCode();

        var report = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(sessionId, report.GetProperty("sessionId").GetString());
        Assert.True(report.TryGetProperty("verified", out _));
        Assert.Equal(1, report.GetProperty("nodesChecked").GetInt32());
        Assert.True(report.TryGetProperty("signaturesVerified", out _));
        Assert.True(report.TryGetProperty("issues", out _));
    }

    [Fact]
    public async Task Get_SessionVerify_NonExistent_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/sessions/non-existent-id/verify");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_ExportSession_ReturnsMarkdown()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new { RootQuestion = "Export test", Framework = "philosophy" });
        createResponse.EnsureSuccessStatusCode();
        var createResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = createResult.GetProperty("sessionId").GetString()!;

        var response = await _client.GetAsync($"/api/sessions/{sessionId}/export?format=markdown");
        Assert.True(response.IsSuccessStatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Export test", content);
    }

    [Fact]
    public async Task Post_ExportReportHtml_IncludesVerifySection()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/sessions", new { RootQuestion = "Report verify test", Framework = "philosophy" });
        createResponse.EnsureSuccessStatusCode();
        var createResult = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = createResult.GetProperty("sessionId").GetString()!;

        var store = _factory.Services.GetRequiredService<IMerkleStore>();
        await store.PutBlobAsync(new DialecticalNode
        {
            SessionId = sessionId,
            NodeType = "question",
            Level = 0,
            SpeakerId = "moderator",
            Content = "Report verify test",
            Timestamp = DateTime.UtcNow,
        });

        var response = await _client.PostAsync($"/api/sessions/{sessionId}/export/report", null);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Integrity Verification", html);
        Assert.Contains("VERIFIED", html);
        Assert.Contains("Nodes Checked", html);
    }
}