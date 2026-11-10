using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SeldonLattice.API.Authentication;

/// <summary>
/// Authentication handler that validates API keys from the Authorization header
/// and issues a ClaimsPrincipal with role claims.
/// </summary>
/// <remarks>
/// The handler reads the "Authorization" header in the format "Bearer {api-key}".
/// API keys and their associated roles are configured in appsettings.json under "ApiKeys".
/// </remarks>
public sealed class ApiKeyAuthHandler(
    IOptionsMonitor<ApiKeyAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISystemClock clock,
    IConfiguration configuration)
    : AuthenticationHandler<ApiKeyAuthOptions>(options, logger, encoder, clock)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var apiKeyEntries = configuration.GetSection("ApiKeys").Get<ApiKeyEntry[]>() ?? [];

        // Try to extract the API key from the Authorization header
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var authHeader) ||
            string.IsNullOrEmpty(authHeader))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing Authorization header"));
        }

        var authHeaderValue = authHeader[0]!;

        string? extractedApiKey;

        // Support both "Bearer {key}" and "ApiKey {key}" formats
        if (authHeaderValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            extractedApiKey = authHeaderValue["Bearer ".Length..].Trim();
        }
        else if (authHeaderValue.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
        {
            extractedApiKey = authHeaderValue["ApiKey ".Length..].Trim();
        }
        else
        {
            // Allow raw API key in header
            extractedApiKey = authHeaderValue.Trim();
        }

        if (string.IsNullOrEmpty(extractedApiKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing API key in Authorization header"));
        }

        // Validate the API key against configured entries
        var matchingEntry = apiKeyEntries.FirstOrDefault(e => ApiKeyComparer.FixedTimeEquals(e.Key, extractedApiKey));
        if (matchingEntry is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        // Build claims with roles
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, extractedApiKey),
            new Claim("api_key", extractedApiKey)
        };

        foreach (var role in matchingEntry.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, Options.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Options.Scheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}