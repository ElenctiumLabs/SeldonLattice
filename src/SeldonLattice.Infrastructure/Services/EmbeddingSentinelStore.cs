using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
namespace SeldonLattice.Infrastructure.Services;

/// <summary>
/// The known-good sentinel vector plus the exact text it was captured for.
///
/// Pairing the text with the vector is what stops the "compared different texts" class of
/// bug: a bare float array cannot tell you which string it describes, so a reference captured
/// for one string silently fails (or silently passes) against another.
/// </summary>
public record SentinelReference(string? SentinelText, float[] Vector);

/// <summary>
/// Reads and writes the embedding continuity reference, and reports whether the stored corpus
/// is empty. Separated from <see cref="OpenAICompatibleEmbeddingService"/> so the bootstrap decision
/// (capture vs. breach) can be tested without an HTTP server.
/// </summary>
public interface IEmbeddingSentinelStore
{
    /// <summary>True when a usable reference exists on disk.</summary>
    Task<bool> HasReferenceAsync(CancellationToken ct = default);

    /// <summary>Loads the reference, or null when absent or unreadable.</summary>
    Task<SentinelReference?> LoadAsync(CancellationToken ct = default);

    /// <summary>Persists the reference. Called once, after the first complete round.</summary>
    Task SaveAsync(string sentinelText, float[] vector, CancellationToken ct = default);

    /// <summary>
    /// True when no stored node carries an embedding. Distinguishes "nothing to protect yet"
    /// (capture is safe and correct) from "a corpus exists but we cannot validate it"
    /// (a breach, because the corpus's space is unknown).
    /// </summary>
    Task<bool> IsCorpusEmptyAsync(CancellationToken ct = default);

    /// <summary>
    /// True when a capture was deferred and is still owed. Persisted, because the deferral has
    /// to survive a restart: a corpus that was BUILT by this server but never got a reference
    /// (the process died mid-round) is not a breach - it is an unfinished bootstrap. Without
    /// this marker the next start would see "non-empty corpus, no reference" and report a
    /// breach for a corpus that is perfectly consistent.
    /// </summary>
    Task<bool> IsCapturePendingAsync(CancellationToken ct = default);

    /// <summary>Records that a capture is owed. Written when the deferral is decided.</summary>
    Task MarkCapturePendingAsync(CancellationToken ct = default);
}

/// <summary>
/// Captures the embedding continuity reference once, after the first complete round.
/// Implemented by <see cref="OpenAICompatibleEmbeddingService"/> and resolved from the kernel by the
/// ascent services, which know when a round has actually completed.
/// </summary>
public interface IEmbeddingSentinel
{
    /// <summary>
    /// Captures the reference if it is pending, i.e. the corpus was empty at startup and no
    /// reference exists yet. A no-op in every other state, so it is safe to call after every
    /// round.
    /// </summary>
    Task TryCaptureReferenceAsync(CancellationToken ct = default);
}

/// <summary>
/// File-backed <see cref="IEmbeddingSentinelStore"/>.
///
/// The reference describes the vector space of the STORED CORPUS, so it must be captured at a
/// moment when the corpus and the server agree - which is exactly bootstrap, when the corpus
/// is empty and about to be built by the running server. Capturing it later, while an
/// older-space corpus already exists, compares the space against itself and passes trivially;
/// that is the failure this type's emptiness check exists to catch.
/// </summary>
public class FileEmbeddingSentinelStore(
    IMerkleStore merkleStore,
    EmbeddingValidationOptions options,
    ILogger<FileEmbeddingSentinelStore> logger) : IEmbeddingSentinelStore
{
    /// <summary>
    /// File shape written by <see cref="SaveAsync"/>. <c>vector</c> is a flat array of floats.
    /// A legacy file that is a bare array is still accepted by <see cref="LoadAsync"/> - it is
    /// treated as "vector only", with the text assumed to be the configured sentinel.
    /// </summary>
    private sealed class ReferenceDocument
    {
        [JsonPropertyName("sentinelText")] public string? SentinelText { get; init; }
        [JsonPropertyName("capturedAt")] public DateTimeOffset? CapturedAt { get; init; }
        [JsonPropertyName("vector")] public float[]? Vector { get; init; }
    }

    private readonly string? _path = ResolvePath(options.SentinelReferencePath);

    /// <summary>
    /// Absolute path to the reference file, or null when unconfigured. Relative paths resolve
    /// against the content root, matching the documented behaviour.
    /// </summary>
    private static string? ResolvePath(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return null;
        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured);
    }

    public Task<bool> HasReferenceAsync(CancellationToken ct = default)
        => Task.FromResult(_path is not null && File.Exists(_path));

    public async Task<SentinelReference?> LoadAsync(CancellationToken ct = default)
    {
        if (_path is null)
            return null;

        if (!File.Exists(_path))
        {
            logger.LogInformation(
                "Embedding continuity reference not present at {Path}.", _path);
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_path, ct);
            return Parse(json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to read embedding continuity reference from {Path}.", _path);
            return null;
        }
    }

    /// <summary>Parses either the current object form or the legacy bare-array form.</summary>
    private SentinelReference? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        // Legacy: a flat JSON array of floats, no text.
        if (json.TrimStart().StartsWith('['))
        {
            var legacy = JsonSerializer.Deserialize<float[]>(json);
            return legacy is { Length: > 0 } ? new SentinelReference(null, legacy) : null;
        }

        var doc = JsonSerializer.Deserialize<ReferenceDocument>(json);
        return doc?.Vector is { Length: > 0 } ? new SentinelReference(doc.SentinelText, doc.Vector) : null;
    }

    public async Task SaveAsync(string sentinelText, float[] vector, CancellationToken ct = default)
    {
        if (_path is null)
        {
            logger.LogWarning(
                "Cannot persist the embedding continuity reference: no " +
                "Embeddings:Validation:SentinelReferencePath is configured. The reference will " +
                "be recaptured on every start, and a vector-space change cannot be detected " +
                "across restarts.");
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var doc = new ReferenceDocument
            {
                SentinelText = sentinelText,
                CapturedAt = DateTimeOffset.UtcNow,
                Vector = vector,
            };
            await File.WriteAllTextAsync(
                _path, JsonSerializer.Serialize(doc), ct);

            // Clear the pending marker now the reference exists. Order matters: the reference is
            // written first, so a crash between the two leaves a marker with a reference - which
            // the loader treats as "reference exists, use it" and the stale marker is harmless.
            // The reverse order could leave a reference-less "not pending" state.
            if (PendingPath is { } pending && File.Exists(pending))
            {
                try
                {
                    File.Delete(pending);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Captured the reference but could not clear {Path}.", pending);
                }
            }

            logger.LogInformation(
                "Captured embedding continuity reference ({Dim}-d) to {Path}. Future runs will " +
                "validate against it; a change of vector space will now be detected.",
                vector.Length, _path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to persist the embedding continuity reference to {Path}.", _path);
        }
    }

    /// <summary>
    /// Path of the pending marker: the reference path with <c>.pending</c> appended. Kept next
    /// to the reference so the two travel together and a wiped data dir clears both.
    /// </summary>
    private string? PendingPath => _path is null ? null : _path + ".pending";

    public Task<bool> IsCapturePendingAsync(CancellationToken ct = default)
        => Task.FromResult(PendingPath is not null && File.Exists(PendingPath));

    public async Task MarkCapturePendingAsync(CancellationToken ct = default)
    {
        if (PendingPath is null)
            return;
        try
        {
            var dir = Path.GetDirectoryName(PendingPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(
                PendingPath,
                $"capture pending since {DateTimeOffset.UtcNow:O}\n", ct);
        }
        catch (Exception ex)
        {
            // Non-fatal: the marker only prevents a false breach after a mid-round crash.
            logger.LogWarning(ex, "Failed to write the sentinel pending marker at {Path}.", PendingPath);
        }
    }

    /// <summary>
    /// Scans stored dialectical nodes for any carrying an embedding. Only runs when the
    /// reference is missing, i.e. at most once per process start.
    /// </summary>
    public async Task<bool> IsCorpusEmptyAsync(CancellationToken ct = default)
    {
        try
        {
            var nodes = await merkleStore.GetAllNodesAsync<DialecticalNode>();
            foreach (var node in nodes)
            {
                ct.ThrowIfCancellationRequested();
                if (node.Embedding is { Length: > 0 })
                    return false;
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Cannot prove the corpus is empty, so do not authorise a capture - treating
            // "unknown" as "empty" would overwrite a valid reference with a live-server
            // capture, which is the exact mistake this check guards against.
            logger.LogWarning(ex,
                "Could not determine whether the stored corpus holds embeddings; treating the " +
                "corpus as NON-empty and refusing to capture a new reference.");
            return false;
        }
    }
}
