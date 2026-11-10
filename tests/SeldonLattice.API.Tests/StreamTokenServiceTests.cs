using SeldonLattice.API.Authentication;
using Xunit;

namespace SeldonLattice.API.Tests;

/// <summary>
/// Verifies the short-lived SSE stream-token flow: issuance, validation, expiry,
/// session-scoping, and single-use behavior.
/// </summary>
public class StreamTokenServiceTests
{
    private static StreamTokenService CreateService(int lifetimeSeconds = 60) =>
        new(Microsoft.Extensions.Options.Options.Create(new StreamTokenOptions
        {
            Secret = "test-secret",
            LifetimeSeconds = lifetimeSeconds,
        }));

    [Fact]
    public void Issue_And_Validate_ValidToken_ReturnsTrue()
    {
        var service = CreateService();
        var token = service.Issue("session-1", "philosophy");
        Assert.True(service.TryValidate(token, "session-1", "philosophy"));
    }

    [Fact]
    public void Validate_WrongSessionId_ReturnsFalse()
    {
        var service = CreateService();
        var token = service.Issue("session-1", "philosophy");
        Assert.False(service.TryValidate(token, "session-2", "philosophy"));
    }

    [Fact]
    public void Validate_WrongModule_ReturnsFalse()
    {
        var service = CreateService();
        var token = service.Issue("session-1", "philosophy");
        Assert.False(service.TryValidate(token, "session-1", "research"));
    }

    [Fact]
    public void Validate_TamperedToken_ReturnsFalse()
    {
        var service = CreateService();
        var token = service.Issue("session-1", "philosophy");

        // Re-encode the token with an altered sessionId in the payload - the
        // signature will no longer match.
        var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
        var parts = raw.Split('|');
        parts[0] = "session-2";
        var tampered = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(string.Join('|', parts)));

        Assert.False(service.TryValidate(tampered, "session-1", "philosophy"));
    }

    [Fact]
    public void Validate_Garbage_ReturnsFalse()
    {
        var service = CreateService();
        Assert.False(service.TryValidate("not-a-token", "session-1", "philosophy"));
        Assert.False(service.TryValidate("", "session-1", "philosophy"));
    }

    [Fact]
    public async Task Validate_ExpiredToken_ReturnsFalse()
    {
        var service = CreateService(lifetimeSeconds: 1);
        var token = service.Issue("session-1", "philosophy");
        // Integer-second expiry can straddle a boundary - wait comfortably past it.
        await Task.Delay(2100);
        Assert.False(service.TryValidate(token, "session-1", "philosophy"));
    }

    [Fact]
    public void Validate_ReplayAttempt_ReturnsFalse()
    {
        var service = CreateService();
        var token = service.Issue("session-1", "philosophy");
        Assert.True(service.TryValidate(token, "session-1", "philosophy"));
        // Single-use - a second attempt with the same token must be rejected.
        Assert.False(service.TryValidate(token, "session-1", "philosophy"));
    }
}
