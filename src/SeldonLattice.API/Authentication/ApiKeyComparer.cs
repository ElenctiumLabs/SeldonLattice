using System.Security.Cryptography;
using System.Text;

namespace SeldonLattice.API.Authentication;

/// <summary>
/// Provides constant-time comparison helpers for secret material.
/// </summary>
internal static class ApiKeyComparer
{
    /// <summary>
    /// Compares two strings in constant time using SHA-256 digests so that
    /// variable-length inputs are normalized before <see cref="CryptographicOperations.FixedTimeEquals"/>.
    /// </summary>
    public static bool FixedTimeEquals(string? a, string? b)
    {
        if (a is null || b is null)
            return false;

        var hashA = SHA256.HashData(Encoding.UTF8.GetBytes(a));
        var hashB = SHA256.HashData(Encoding.UTF8.GetBytes(b));
        return CryptographicOperations.FixedTimeEquals(hashA, hashB);
    }
}