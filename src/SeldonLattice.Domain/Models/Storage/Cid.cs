using System.Security.Cryptography;
using System.Text;
using MessagePack;

namespace SeldonLattice.Domain.Models.Storage;

/// <summary>
/// Content Identifier (CID) for Merkle DAG nodes.
/// Uses SHA3-256 hashing to create immutable, content-addressable identifiers.
/// </summary>
[MessagePackObject]
public record Cid
{
    /// <summary>
    /// The base64url-encoded hash value.
    /// </summary>
    [Key(0)] public string Hash { get; init; } = "";

    /// <summary>
    /// The hash algorithm used (default: sha3-256).
    /// </summary>
    [Key(1)] public string Algorithm { get; init; } = "sha3-256";

    /// <summary>
    /// Decodes the hash into raw bytes (ignores the algorithm prefix).
    /// </summary>
    [IgnoreMember]
    public byte[] RawHash
    {
        get
        {
            var s = Hash.Replace('-', '+').Replace('_', '/');
            s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            return Convert.FromBase64String(s);
        }
    }

    /// <summary>
    /// Parameterless constructor for MessagePack deserialization.
    /// </summary>
    public Cid() { }

    /// <summary>
    /// Creates a CID from a raw hash byte array.
    /// </summary>
    private Cid(byte[] hash) { Hash = Base64UrlEncode(hash); }

    /// <summary>
    /// Creates a CID from an existing hash string and algorithm.
    /// </summary>
    private Cid(string hash, string algo) { Hash = hash; Algorithm = algo; }

    /// <summary>
    /// Computes a CID from arbitrary binary data using SHA3-256 (post-quantum-resistant).
    /// Legacy "sha256" CIDs remain parseable for backwards compatibility during migration.
    /// </summary>
    /// <param name="data">The data to hash.</param>
    /// <returns>A new CID identifying the content.</returns>
    public static Cid Compute(byte[] data)
    {
        using var sha = SHA3_256.Create();
        return new Cid(sha.ComputeHash(data));
    }

    /// <summary>
    /// Computes a CID from a string by first encoding it as UTF-8 bytes.
    /// </summary>
    /// <param name="data">The string to hash.</param>
    /// <returns>A new CID identifying the string content.</returns>
    public static Cid Compute(string data) => Compute(Encoding.UTF8.GetBytes(data));

    /// <summary>
    /// Known algorithm prefixes, ordered longest-first so multi-part names
    /// like "sha3-256" win over shorter prefixes during parsing.
    /// </summary>
    private static readonly string[] KnownAlgorithms = ["sha3-256", "sha256"];

    /// <summary>
    /// Parses a CID from its string representation (format: "algorithm-hash").
    /// </summary>
    /// <param name="str">The string representation like "sha256-AbCdEf..."</param>
    /// <returns>A CID instance.</returns>
    public static Cid FromString(string str)
    {
        if (string.IsNullOrEmpty(str)) return new Cid(str, "sha3-256");

        foreach (var algo in KnownAlgorithms)
        {
            var prefix = algo + "-";
            if (str.StartsWith(prefix, StringComparison.Ordinal))
            {
                return new Cid(str[prefix.Length..], algo);
            }
        }

        return new Cid(str, "sha3-256");
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    private static byte[] Base64UrlDecode(string str)
    {
        var s = str.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }

    /// <summary>
    /// Returns the string representation (algorithm-hash format).
    /// </summary>
    public override string ToString() => $"{Algorithm}-{Hash}";
}