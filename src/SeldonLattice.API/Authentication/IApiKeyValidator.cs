namespace SeldonLattice.API.Authentication;

/// <summary>
/// Default API key validator that checks keys against configured entries.
/// </summary>
public interface IApiKeyValidator
{
    /// <summary>
    /// Validates an API key and returns the associated roles, or null if invalid.
    /// </summary>
    string[]? ValidateApiKey(string apiKey);
}