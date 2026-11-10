using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SeldonLattice.API.Authentication;
using Xunit;

namespace SeldonLattice.API.Tests;

/// <summary>
/// Verifies that LLM-compute and module-creation endpoints require an AdminOnly API key.
/// </summary>
public class ProtectedEndpointTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private HttpClient CreateClient() => factory.CreateClient();

    [Fact]
    public async Task Post_Ascend_WithoutAuth_ReturnsUnauthorized()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/ascend", new { Level = 1, Theme = "What is justice?" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_AscendStream_WithoutAuth_ReturnsUnauthorized()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/ascend/stream", new { Level = 1, Theme = "What is justice?" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Modules_WithoutAuth_ReturnsUnauthorized()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/modules", new
        {
            Key = "test-module",
            Name = "Test Module",
            Agents = new[]
            {
                new { Name = "Alpha", Framework = "classical", Description = "first" },
                new { Name = "Beta", Framework = "classical", Description = "second" }
            }
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Ascend_WithDialecticianKey_PassesAuthorization()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "ApiKey dev-key-dialectician");
        var response = await client.PostAsJsonAsync("/api/ascend", new { Level = 1, Theme = "What is justice?" });

        // Authorization must pass (dialectician role — AscentOnly). The ascent itself
        // may fail with 500 in the test environment where no LLM is configured.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Ascend_WithAdminKey_ReturnsForbidden()
    {
        // Non-overlap: admin is not dialectician. No key carries more than its
        // own roles — docs/audits/api-hardening-2026-10-20.md (no carte blanche).
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "ApiKey dev-key-admin");
        var response = await client.PostAsJsonAsync("/api/ascend", new { Level = 1, Theme = "What is justice?" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Modules_WithReaderKey_ReturnsForbidden()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "ApiKey dev-key-reader");
        var response = await client.PostAsJsonAsync("/api/modules", new
        {
            Key = "test-module",
            Name = "Test Module",
            Agents = new[]
            {
                new { Name = "Alpha", Framework = "classical", Description = "first" },
                new { Name = "Beta", Framework = "classical", Description = "second" }
            }
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_StreamToken_WithoutAuth_ReturnsUnauthorized()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/stream-token", new { SessionId = "s1" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_StreamToken_WithStreamKey_ReturnsToken()
    {
        // Minting requires the `stream` role as a plain role check — a stream
        // *token* must not be able to mint another token.
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "ApiKey dev-key-dialectician");
        var response = await client.PostAsJsonAsync("/api/auth/stream-token", new { SessionId = "s1", Module = "classical" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(body.TryGetProperty("streamToken", out var tokenProp));
        Assert.False(string.IsNullOrEmpty(tokenProp.GetString()));
    }

    [Fact]
    public async Task Post_AscendStream_WithValidStreamToken_PassesAuthorization()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "ApiKey dev-key-dialectician");
        var tokenResponse = await client.PostAsJsonAsync("/api/auth/stream-token", new { SessionId = "s1", Module = "classical" });
        var tokenBody = await tokenResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var streamToken = tokenBody.GetProperty("streamToken").GetString()!;

        var streamClient = CreateClient();
        var response = await streamClient.PostAsJsonAsync(
            $"/api/ascend/stream?sessionId=s1&streamToken={Uri.EscapeDataString(streamToken)}",
            new { Level = 1, Theme = "What is justice?" });

        // Authorization must pass. The ascent itself may fail (500) without an LLM configured.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_AscendStream_WithExpiredStreamToken_ReturnsUnauthorized()
    {
        // Issue a token, then validate it through the service directly to confirm
        // a spent token is rejected (simulating an already-used / expired token).
        var sessionId = $"s-{Guid.NewGuid():N}";
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "ApiKey dev-key-dialectician");
        var tokenResponse = await client.PostAsJsonAsync("/api/auth/stream-token", new { SessionId = sessionId, Module = "classical" });
        var tokenBody = await tokenResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var streamToken = tokenBody.GetProperty("streamToken").GetString()!;

        // Spend the token once via the service (mirrors single-use enforcement).
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<StreamTokenService>();
        Assert.True(service.TryValidate(streamToken, sessionId, "classical"));

        // Replay with the same token must be rejected → 401.
        var streamClient = CreateClient();
        var response = await streamClient.PostAsJsonAsync(
            $"/api/ascend/stream?sessionId={sessionId}&streamToken={Uri.EscapeDataString(streamToken)}",
            new { Level = 1, Theme = "What is justice?" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
