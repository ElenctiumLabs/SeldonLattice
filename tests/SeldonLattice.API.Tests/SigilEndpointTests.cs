using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace SeldonLattice.API.Tests;

/// <summary>
/// The algorithm selector must accept every spelling the endpoint itself advertises
/// in its 400 message (fractal_tree, mandala …): separators are stripped rather
/// than rewritten, so snake-case, kebab-case and PascalCase all resolve to the same
/// member (deferred fix 2, handoff_2026-10-20).
/// </summary>
public class SigilEndpointTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    private HttpClient CreateClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "ApiKey dev-key-reader");
        return client;
    }

    [Theory]
    [InlineData("made_up")]
    [InlineData("fractal-treee")]
    public async Task Get_Sigil_WithUnknownAlgorithm_ReturnsBadRequest(string spelling)
    {
        var response = await CreateClient().GetAsync(
            $"/api/sigil/test-cid-for-sigil?algorithm={Uri.EscapeDataString(spelling)}&format=json");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_Sigil_WithoutKey_ReturnsUnauthorized()
    {
        var response = await factory.CreateClient().GetAsync("/api/sigil/test-cid-for-sigil?format=json");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Regression finding : arbitrary CIDs whose raw hash is shorter than
    // 8 bytes used to crash HashRng (ArgumentException) -> 500.
    [Theory]
    [InlineData("testcid")]
    [InlineData("abc")]
    public async Task Get_Sigil_WithArbitraryShortHashCid_ReturnsOk(string cid)
    {
        var response = await CreateClient().GetAsync(
            $"/api/sigil/{cid}?algorithm=fractal_tree&size=64&format=json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
