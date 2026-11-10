using System.Text.Json;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Infrastructure.Pqc;

/// <summary>
/// NIST ML-DSA-65 (CRYSTALS-Dilithium level 3) post-quantum signer.
/// Uses BouncyCastle's PQC implementation. Keys are persisted per signer id
/// to a key directory so signatures remain reproducible across restarts.
/// </summary>
public class MLDsaSigner : IPqcSigner
{
    public const string AlgorithmName = "ML-DSA-65";

    private static readonly MLDsaParameters Parameters = MLDsaParameters.ml_dsa_65;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly string _keyDir;
    private readonly ILogger<MLDsaSigner> _logger;
    private readonly object _keyLock = new();
    private readonly Dictionary<string, MLDsaPrivateKeyParameters> _cache = new();

    /// <summary>
    /// Initializes the signer with a directory for persistent key storage.
    /// </summary>
    /// <param name="keyDir">Directory where per-signer keys are stored. Created if missing.</param>
    /// <param name="logger">Logger.</param>
    public MLDsaSigner(string keyDir, ILogger<MLDsaSigner> logger)
    {
        _keyDir = keyDir;
        _logger = logger;
        Directory.CreateDirectory(_keyDir);
    }

    /// <inheritdoc />
    public Task<AgentSignature> SignAsync(string signerId, byte[] content, CancellationToken ct = default)
    {
        var key = GetOrCreateKey(signerId);
        var signer = new Org.BouncyCastle.Crypto.Signers.MLDsaSigner(Parameters, true);
        signer.Init(true, key);

        signer.BlockUpdate(content, 0, content.Length);
        var sig = signer.GenerateSignature();

        var signature = new AgentSignature
        {
            Algorithm = AlgorithmName,
            SignerId = signerId,
            PublicKey = Convert.ToBase64String(key.GetPublicKey().GetEncoded()),
            Signature = Convert.ToBase64String(sig),
            SignedCid = ComputeReference(content),
            Timestamp = DateTime.UtcNow
        };

        return Task.FromResult(signature);
    }

    /// <inheritdoc />
    public Task<bool> VerifyAsync(AgentSignature signature, byte[] content, CancellationToken ct = default)
    {
        if (signature is null || string.IsNullOrEmpty(signature.Signature) || string.IsNullOrEmpty(signature.PublicKey))
        {
            return Task.FromResult(false);
        }

        try
        {
            var publicKey = MLDsaPublicKeyParameters.FromEncoding(Parameters, Convert.FromBase64String(signature.PublicKey));
            var verifier = new Org.BouncyCastle.Crypto.Signers.MLDsaSigner(Parameters, false);
            verifier.Init(false, publicKey);
            verifier.BlockUpdate(content, 0, content.Length);
            return Task.FromResult(verifier.VerifySignature(Convert.FromBase64String(signature.Signature)));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ML-DSA verification failed for signer {SignerId}", signature.SignerId);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Generates the signed-content reference string used in the signature record.
    /// For content that is a serialized node this is its CID; the caller is
    /// responsible for passing content whose CID is deterministic.
    /// </summary>
    private static string ComputeReference(byte[] content) =>
        Cid.Compute(content).ToString();

    private MLDsaPrivateKeyParameters GetOrCreateKey(string signerId)
    {
        lock (_keyLock)
        {
            if (_cache.TryGetValue(signerId, out var cached))
            {
                return cached;
            }

            var path = KeyPath(signerId);
            MLDsaPrivateKeyParameters key;
            if (File.Exists(path))
            {
                try
                {
                    var serialized = JsonSerializer.Deserialize<KeyFile>(File.ReadAllText(path), JsonOptions);
                    key = MLDsaPrivateKeyParameters.FromEncoding(Parameters, Convert.FromBase64String(serialized!.PrivateKey!));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load ML-DSA key for {SignerId}; regenerating", signerId);
                    key = GenerateKey(signerId, path);
                }
            }
            else
            {
                key = GenerateKey(signerId, path);
            }

            _cache[signerId] = key;
            return key;
        }
    }

    private MLDsaPrivateKeyParameters GenerateKey(string signerId, string path)
    {
        var generator = new MLDsaKeyPairGenerator();
        generator.Init(new MLDsaKeyGenerationParameters(new SecureRandom(), Parameters));
        var pair = generator.GenerateKeyPair();

        var key = (MLDsaPrivateKeyParameters)pair.Private;
        var file = new KeyFile
        {
            PrivateKey = Convert.ToBase64String(key.GetEncoded()),
            PublicKey = Convert.ToBase64String(key.GetPublicKey().GetEncoded())
        };
        File.WriteAllText(path, JsonSerializer.Serialize(file, JsonOptions));
        _logger.LogInformation("Generated ML-DSA-65 key for {SignerId}", signerId);
        return key;
    }

    private string KeyPath(string signerId) =>
        Path.Combine(_keyDir, Sanitize(signerId) + ".mldsa.json");

    private static string Sanitize(string value) =>
        string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

    private sealed class KeyFile
    {
        public string? PrivateKey { get; init; }
        public string? PublicKey { get; init; }
    }
}