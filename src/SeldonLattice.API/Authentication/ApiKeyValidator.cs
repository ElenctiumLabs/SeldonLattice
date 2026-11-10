namespace SeldonLattice.API.Authentication;

/// <summary>
/// Default implementation of <see cref="IApiKeyValidator"/> that reads from configuration.
/// </summary>
public sealed class ApiKeyValidator(IConfiguration configuration) : IApiKeyValidator
{
    private ApiKeyEntry[]? _cachedEntries;

    public string[]? ValidateApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
            return null;

        _cachedEntries ??= configuration.GetSection("ApiKeys").Get<ApiKeyEntry[]>() ?? [];

        var entry = _cachedEntries.FirstOrDefault(e => ApiKeyComparer.FixedTimeEquals(e.Key, apiKey));
        return entry?.Roles;
    }
}