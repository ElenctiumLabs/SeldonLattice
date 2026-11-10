using Microsoft.AspNetCore.Authentication;

namespace SeldonLattice.API.Authentication;

/// <summary>
/// Options for API key authentication scheme.
/// </summary>
public sealed class ApiKeyAuthOptions : AuthenticationSchemeOptions
{
    /// <summary>The scheme name used for API key authentication.</summary>
    public const string DefaultScheme = "ApiKey";
    /// <summary>Gets or sets the scheme name. Defaults to <see cref="DefaultScheme"/>.</summary>
    public string Scheme => DefaultScheme;
    /// <summary>Header name for the API key (default: Authorization with Bearer prefix).</summary>
    public string HeaderName { get; set; } = "Authorization";
}