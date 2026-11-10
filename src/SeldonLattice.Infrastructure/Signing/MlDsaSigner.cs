using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Crypto.Parameters;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using BcMlDsa = Org.BouncyCastle.Crypto.Signers.MLDsaSigner;

namespace SeldonLattice.Infrastructure.Signing;

/// <summary>
/// Options for <see cref="MlDsaSigner"/>.
/// </summary>
public sealed record MlDsaSignerOptions
{
    /// <summary>
    /// Directory holding one 32-byte ML-DSA seed per signer identity. When null, keys live in
    /// memory for the lifetime of the process (stable within a run, not across runs).
    /// </summary>
    public string? KeyDirectory { get; init; }

    /// <summary>
    /// Parameter set. Defaults to ML-DSA-65 (NIST security category 3).
    /// </summary>
    public MLDsaParameters Parameters { get; init; } = MLDsaParameters.ml_dsa_65;
}

/// <summary>
/// Reference <see cref="IPqcSigner"/> implementation backed by BouncyCastle's ML-DSA.
/// <para>
/// Signatures are deterministic for a given key and content, and carry the public key, so a
/// verifier needs no key lookup. Keys are per signer identity and stable: an identity's seed
/// is created once, then reloaded from <see cref="MlDsaSignerOptions.KeyDirectory"/>.
/// </para>
/// <para>
/// This is a reference implementation, not a key management system. A seed file on disk is
/// only as safe as the filesystem; production deployments should back
/// <see cref="IPqcSigner"/> with a KMS or HSM and keep private keys out of process.
/// </para>
/// </summary>
public sealed class MlDsaSigner : IPqcSigner
{
    /// <summary>Algorithm name written into every produced signature.</summary>
    public const string AlgorithmName = "ML-DSA-65";

    private static readonly Regex SignerIdPattern =
        new("^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly MlDsaSignerOptions _options;
    private readonly ConcurrentDictionary<string, Lazy<MLDsaPrivateKeyParameters>> _keys =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a signer.
    /// </summary>
    /// <param name="options">Key storage options. Defaults to in-memory keys.</param>
    public MlDsaSigner(MlDsaSignerOptions? options = null)
    {
        _options = options ?? new MlDsaSignerOptions();

        if (_options.KeyDirectory is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            TryRestrictPermissions(directory);
        }
    }

    /// <inheritdoc />
    public Task<AgentSignature> SignAsync(string signerId, byte[] content, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signerId);
        ArgumentNullException.ThrowIfNull(content);
        ValidateSignerId(signerId);

        var key = _keys.GetOrAdd(signerId, id => new Lazy<MLDsaPrivateKeyParameters>(
            () => LoadOrCreateKey(id), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        var signer = new BcMlDsa(_options.Parameters, deterministic: true);
        signer.Init(forSigning: true, key);
        signer.BlockUpdate(content, 0, content.Length);
        var signature = signer.GenerateSignature();

        return Task.FromResult(new AgentSignature
        {
            Algorithm = AlgorithmName,
            SignerId = signerId,
            PublicKey = Convert.ToBase64String(key.GetPublicKeyEncoded()),
            Signature = Convert.ToBase64String(signature),
            SignedCid = Cid.Compute(content).ToString(),
            Timestamp = DateTime.UtcNow,
        });
    }

    /// <inheritdoc />
    public Task<bool> VerifyAsync(AgentSignature signature, byte[] content, CancellationToken ct = default)
    {
        if (!string.Equals(signature.Algorithm, AlgorithmName, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(false);
        }

        if (string.IsNullOrEmpty(signature.PublicKey) || string.IsNullOrEmpty(signature.Signature))
        {
            return Task.FromResult(false);
        }

        try
        {
            var publicKey = MLDsaPublicKeyParameters.FromEncoding(
                _options.Parameters, Convert.FromBase64String(signature.PublicKey));

            var verifier = new BcMlDsa(_options.Parameters, deterministic: true);
            verifier.Init(forSigning: false, publicKey);
            verifier.BlockUpdate(content, 0, content.Length);
            return Task.FromResult(verifier.VerifySignature(Convert.FromBase64String(signature.Signature)));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or CryptographicException)
        {
            // Malformed key or signature material is a verification failure, not a crash.
            return Task.FromResult(false);
        }
    }

    private MLDsaPrivateKeyParameters LoadOrCreateKey(string signerId)
    {
        var seed = ReadSeed(signerId);
        if (seed is not null)
        {
            return MLDsaPrivateKeyParameters.FromSeed(_options.Parameters, seed);
        }

        seed = RandomNumberGenerator.GetBytes(32);
        WriteSeed(signerId, seed);
        return MLDsaPrivateKeyParameters.FromSeed(_options.Parameters, seed);
    }

    private byte[]? ReadSeed(string signerId)
    {
        if (_options.KeyDirectory is not { Length: > 0 } directory)
        {
            return null;
        }

        var path = SeedPath(directory, signerId);
        if (!File.Exists(path))
        {
            return null;
        }

        var seed = File.ReadAllBytes(path);
        if (seed.Length != 32)
        {
            throw new InvalidOperationException(
                $"ML-DSA seed for signer '{signerId}' at {path} is {seed.Length} bytes, expected 32.");
        }

        return seed;
    }

    private void WriteSeed(string signerId, byte[] seed)
    {
        if (_options.KeyDirectory is not { Length: > 0 } directory)
        {
            return;
        }

        var path = SeedPath(directory, signerId);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, seed);
        TryRestrictPermissions(temp);
        File.Move(temp, path, overwrite: true);
    }

    private static string SeedPath(string directory, string signerId) =>
        Path.Combine(directory, signerId + ".mldsa.seed");

    private static void ValidateSignerId(string signerId)
    {
        // Identities become file names when a key directory is configured, so traversal
        // and separator characters must not be accepted.
        if (!SignerIdPattern.IsMatch(signerId))
        {
            throw new ArgumentException(
                "Signer identity must match ^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$ so it can be used as a key name.",
                nameof(signerId));
        }
    }

    private static void TryRestrictPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Best effort: a shared volume may not support POSIX modes.
        }
    }
}
