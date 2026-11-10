using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace SeldonLattice.API.Tests;

/// <summary>
/// Role-matrix probes for the granular authz model: every dedicated key must be FORBIDDEN
/// on routes owned by a different role, so no committed key is carte blanche. Also
/// covers same-role positive controls (authorization passes; business outcome not asserted).
/// </summary>
public class RoleMatrixEndpointTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private HttpClient ClientFor(string key)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"ApiKey {key}");
        return client;
    }

    private static async Task AssertForbidden(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 403 Forbidden but got {(int)response.StatusCode}: {body}");
    }

    // ---- Non-overlap: authenticated but wrong role → 403 ----------------------------------------------

    [Theory]
    [InlineData("dev-key-admin")]       // admin  ≠ reader
    [InlineData("dev-key-ingest")]      // ingest ≠ reader
    [InlineData("dev-key-settings")]    // settings ≠ reader
    [InlineData("dev-key-dialectician")] // dialectician ≠ reader
    public async Task Get_Nodes_WithNonReaderKey_ReturnsForbidden(string key)
    {
        var response = await ClientFor(key).GetAsync("/api/nodes?sessionId=matrix");
        await AssertForbidden(response);
    }

    [Theory]
    [InlineData("dev-key-admin")]        // admin ≠ writer
    [InlineData("dev-key-reader")]       // reader ≠ writer
    [InlineData("dev-key-ingest")]       // ingest ≠ writer
    [InlineData("dev-key-settings")]     // settings ≠ writer
    [InlineData("dev-key-dialectician")] // dialectician ≠ writer
    public async Task Post_Sessions_WithNonWriterKey_ReturnsForbidden(string key)
    {
        var response = await ClientFor(key).PostAsJsonAsync(
            "/api/sessions", new { RootQuestion = "matrix", Framework = "classical" });
        await AssertForbidden(response);
    }

    [Theory]
    [InlineData("dev-key-admin")]   // admin ≠ stream
    [InlineData("dev-key-reader")]  // reader ≠ stream
    [InlineData("dev-key-ingest")]  // ingest ≠ stream
    [InlineData("dev-key-settings")] // settings ≠ stream
    public async Task Post_StreamToken_WithNonStreamKey_ReturnsForbidden(string key)
    {
        var response = await ClientFor(key).PostAsJsonAsync(
            "/api/auth/stream-token", new { SessionId = "s1", Module = "classical" });
        await AssertForbidden(response);
    }

    [Theory]
    [InlineData("dev-key-reader")]       // reader ≠ admin
    [InlineData("dev-key-ingest")]       // ingest ≠ admin
    [InlineData("dev-key-settings")]     // settings ≠ admin
    [InlineData("dev-key-dialectician")] // dialectician ≠ admin
    [InlineData("dev-key-full")]         // the UI composite key deliberately has no admin (D3)
    public async Task Get_AdminMigrate_WithNonAdminKey_ReturnsForbidden(string key)
    {
        var response = await ClientFor(key).GetAsync("/api/admin/migrate");
        await AssertForbidden(response);
    }

    [Theory]
    [InlineData("dev-key-admin")]        // admin ≠ dialectician
    [InlineData("dev-key-reader")]       // reader ≠ dialectician
    [InlineData("dev-key-ingest")]       // ingest ≠ dialectician
    [InlineData("dev-key-settings")]     // settings ≠ dialectician
    public async Task Post_Ascend_WithNonDialecticianKey_ReturnsForbidden(string key)
    {
        var response = await ClientFor(key).PostAsJsonAsync(
            "/api/ascend", new { Level = 1, Theme = "What is justice?" });
        await AssertForbidden(response);
    }

    [Theory]
    [InlineData("dev-key-admin")]    // admin ≠ settings
    [InlineData("dev-key-reader")]   // reader ≠ settings
    [InlineData("dev-key-ingest")]   // ingest ≠ settings
    [InlineData("dev-key-dialectician")] // dialectician ≠ settings
    public async Task Get_Settings_WithNonSettingsKey_ReturnsForbidden(string key)
    {
        var response = await ClientFor(key).GetAsync("/api/settings/classical");
        await AssertForbidden(response);
    }

    [Theory]
    [InlineData("dev-key-ingest")]   // ingest ≠ reader
    [InlineData("dev-key-settings")] // settings ≠ reader
    public async Task Get_IngestionDocuments_WithNonReaderKey_ReturnsForbidden(string key)
    {
        var response = await ClientFor(key).GetAsync("/api/ingestion/documents");
        await AssertForbidden(response);
    }

    // ---- Same-role positive controls: authorization must pass ------------------------------------

    [Fact]
    public async Task Get_Nodes_WithReaderKey_PassesAuthorization()
    {
        var response = await ClientFor("dev-key-reader").GetAsync("/api/nodes?sessionId=matrix-none");
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_AdminStatus_WithAdminKey_PassesAuthorization()
    {
        var response = await ClientFor("dev-key-admin").GetAsync("/api/admin/status");
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Settings_WithSettingsKey_PassesAuthorization()
    {
        var response = await ClientFor("dev-key-settings").GetAsync("/api/settings/classical");
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_AuthValidate_WithDedicatedKey_ReturnsOwnRolesOnly()
    {
        var response = await ClientFor("dev-key-ingest").GetAsync("/api/auth/validate");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var roles = body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToArray();
        Assert.Contains("ingest", roles);
        Assert.DoesNotContain("admin", roles);
        Assert.False(body.GetProperty("isAdmin").GetBoolean());
    }
}
