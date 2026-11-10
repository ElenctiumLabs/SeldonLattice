using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace SeldonLattice.API.Authentication;

/// <summary>
/// Issues and validates short-lived, single-use HMAC-signed tokens for SSE streaming,
/// scoped to a specific sessionId + module. Used because EventSource cannot attach
/// an Authorization header.
/// </summary>
public sealed class StreamTokenService
{
    private readonly byte[] _key;
    private readonly int _lifetimeSeconds;
    /// <summary>Accepted tokens → expiry unix seconds, for single-use enforcement and eviction.</summary>
    private readonly Dictionary<string, long> _spentTokens = new(StringComparer.Ordinal);

    /// <summary>Bound on the spent-token map so a long-lived process cannot grow it forever.</summary>
    private const int MaxSpentTokens = 10_000;

    public StreamTokenService(IOptions<StreamTokenOptions> options)
    {
        var secret = string.IsNullOrEmpty(options.Value.Secret)
            ? "dev-stream-token-secret"
            : options.Value.Secret;
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        _lifetimeSeconds = Math.Max(1, options.Value.LifetimeSeconds);
    }

    /// <summary>
    /// Issues a token covering {sessionId, module, expiry} signed with an HMAC-SHA256 key.
    /// </summary>
    public string Issue(string sessionId, string module)
    {
        var expiry = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _lifetimeSeconds;
        var payload = $"{sessionId}|{module}|{expiry}";
        var signature = Sign(payload);
        var raw = $"{payload}|{signature}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    /// <summary>
    /// Validates the token: signature, expiry, session/module match, and single-use.
    /// Returns false on any failure.
    /// </summary>
    public bool TryValidate(string token, string sessionId, string module)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(sessionId))
            return false;

        string raw;
        try
        {
            raw = Encoding.UTF8.GetString(Convert.FromBase64String(token));
        }
        catch (FormatException)
        {
            return false;
        }

        var parts = raw.Split('|');
        if (parts.Length != 4)
            return false;

        var tokenSessionId = parts[0];
        var tokenModule = parts[1];
        var tokenExpiry = parts[2];
        var tokenSignature = parts[3];

        var payload = $"{tokenSessionId}|{tokenModule}|{tokenExpiry}";

        byte[] providedSigBytes;
        byte[] expectedSigBytes;
        try
        {
            providedSigBytes = Convert.FromHexString(tokenSignature);
            expectedSigBytes = Convert.FromHexString(Sign(payload));
        }
        catch (FormatException)
        {
            return false;
        }
        if (!CryptographicOperations.FixedTimeEquals(providedSigBytes, expectedSigBytes))
            return false;

        if (!string.Equals(tokenSessionId, sessionId, StringComparison.Ordinal))
            return false;
        if (!string.Equals(tokenModule, module, StringComparison.Ordinal))
            return false;

        if (!long.TryParse(tokenExpiry, out var expiryUnix))
            return false;
        if (expiryUnix < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            return false;

        // Single-use: once a token is accepted it is spent. Entries carry their expiry so
        // stale ones can be evicted, and the map is bounded as a backstop.
        lock (_spentTokens)
        {
            if (_spentTokens.ContainsKey(token))
                return false;

            if (_spentTokens.Count >= MaxSpentTokens)
            {
                var cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                foreach (var stale in _spentTokens.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList())
                    _spentTokens.Remove(stale);
                // Still full of unexpired tokens: drop the oldest entries defensively.
                while (_spentTokens.Count >= MaxSpentTokens)
                    _spentTokens.Remove(_spentTokens.Keys.First());
            }

            _spentTokens[token] = expiryUnix;
        }

        return true;
    }

    private string Sign(string payload)
    {
        using var hmac = new HMACSHA256(_key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }
}
