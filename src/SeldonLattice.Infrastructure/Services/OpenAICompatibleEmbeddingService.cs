using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using SeldonLattice.Domain.Utilities;

namespace SeldonLattice.Infrastructure.Services;

/// <summary>
/// Custom embedding service for OpenAI-compatible providers, including llama.cpp.
/// Handles response formats which may omit the "model" field, causing
/// NullReferenceException in the OpenAI SDK v2 deserializer.
///
/// Validates every returned vector before it reaches the metric stack. This matters because
/// the entire scoring geometry (Betweenness, Advance, IrreducibleTension, JoinNovelty) is
/// cosine-based, and nothing downstream checks shape or scale. A wrongly pooled or
/// differently normalised vector produces plausible numbers and wrong scores - a failure that
/// looks like a result.
///
/// Two checks, doing different jobs:
///   - SHAPE (dimension + L2 norm): catches a wrong model, wrong pooling, or disabled
///     normalisation. Cheap, runs on every response.
///   - CONTINUITY (cosine against a stored reference for a fixed sentinel string): catches a
///     change of VECTOR SPACE that leaves shape intact. Dimension and norm can both be
///     perfect while the space is different - which is exactly what a provider migration can
///     do, and what the shape check alone will not notice.
/// </summary>
public class OpenAICompatibleEmbeddingService(
    HttpClient httpClient,
    string endpoint,
    string model,
    string apiKey,
    ILogger<OpenAICompatibleEmbeddingService> logger,
    EmbeddingValidationOptions? validation = null,
    IEmbeddingSentinelStore? sentinelStore = null)
    : ITextEmbeddingGenerationService, IEmbeddingGenerator<string, Embedding<float>>, IEmbeddingSentinel
{
    private readonly string _endpoint = endpoint.TrimEnd('/');
    private readonly EmbeddingValidationOptions _validation = validation ?? new EmbeddingValidationOptions();

    /// <summary>
    /// Reference storage. Optional so unit tests can construct the service without a store;
    /// when absent the service degrades to shape-only validation and says so once.
    /// </summary>
    private readonly IEmbeddingSentinelStore? _sentinelStore = sentinelStore;

    /// <summary>
    /// Guards so the continuity check runs once per SERVICE INSTANCE, not once per process.
    /// Instance state matters: a process can hold more than one embedding service (different
    /// endpoints or models), and each describes its own vector space. A static flag would let
    /// the first instance silence validation for the rest - and in tests it would let one
    /// test's check suppress another's, hiding exactly the failure the check exists to catch.
    /// </summary>
    private bool _continuityChecked;
    private bool _continuityWarned;

    /// <summary>
    /// Set when the corpus was empty at the first continuity check, meaning the reference is
    /// owed and will be captured after the first complete round. Cleared once captured.
    /// </summary>
    private bool _capturePending;

    /// <summary>Guards the capture against concurrent rounds finishing at once.</summary>
    private readonly SemaphoreSlim _captureLock = new(1, 1);

    public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

    public async Task<IList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(
        IList<string> data,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var results = await PostEmbeddingsAsync(data, cancellationToken);

        // Continuity: embed the sentinel text separately and compare to the stored reference.
        //
        // This calls PostEmbeddingsAsync directly, NOT the public GenerateEmbeddingAsync
        // overload. GenerateEmbeddingAsync delegates back into GenerateEmbeddingsAsync, which
        // re-enters this block - and _continuityChecked is only set once ValidateContinuity has
        // run, so the flag cannot break the cycle. The result was unbounded mutual recursion
        // issuing one HTTP request per level until the stack overflowed. Going straight to the
        // transport method makes the extra call a plain one-shot request.
        if (!_continuityChecked && results.Count > 0)
        {
            List<ReadOnlyMemory<float>> sentinel;
            try
            {
                sentinel = await PostEmbeddingsAsync([_validation.SentinelText], cancellationToken);
            }
            catch (Exception ex)
            {
                // The sentinel is a diagnostic, so a transport failure fetching it must not
                // fail the caller's real embedding request - that would trade a silent problem
                // for a loud outage. Note the catch wraps ONLY the fetch: ValidateContinuity is
                // called outside it, so a genuine vector-space mismatch still propagates per
                // ThrowOnContinuityFailure.
                _continuityChecked = true;
                logger.LogWarning(ex,
                    "Continuity sentinel request failed; continuity check skipped for this instance.");
                ValidateShape(results);
                return results;
            }

            if (sentinel.Count > 0 && sentinel[0].Length > 0)
                await ValidateContinuityAsync(sentinel[0].ToArray(), cancellationToken);
        }

        ValidateShape(results);

        return results;
    }

    /// <summary>
    /// Performs the embedding HTTP round-trip (with retries) and returns the raw vectors,
    /// with no validation and no continuity check. Kept separate so
    /// <see cref="GenerateEmbeddingsAsync"/> can fetch the sentinel vector without
    /// re-entering itself.
    /// </summary>
    private async Task<List<ReadOnlyMemory<float>>> PostEmbeddingsAsync(
        IList<string> data,
        CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            model,
            input = data
        };

        logger.LogInformation("OpenAI-compatible embedding request: model={Model}, inputCount={Count}", model, data.Count);

        // Retry logic for transient errors (model unloaded, 500s, 429 rate limits)
        const int maxRetries = 3;
        const int retryDelayMs = 2000;
        HttpResponseMessage? response = null;
        string? errorBody = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            // HttpClient does not allow the same HttpRequestMessage to be sent twice.
            // A fresh message is required per attempt.
            var attemptRequest = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint}/embeddings")
            {
                Content = JsonContent.Create(requestBody)
            };
            attemptRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
            response = await httpClient.SendAsync(attemptRequest, cancellationToken);

            if (response.IsSuccessStatusCode)
                break;

            errorBody = await response.Content.ReadAsStringAsync(cancellationToken);

            // 429 Rate Limit: respect Retry-After header with exponential backoff fallback
            if ((int)response.StatusCode == 429)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta;
                var delay = retryAfter ?? TimeSpan.FromMilliseconds(retryDelayMs * Math.Pow(2, attempt - 1));

                logger.LogWarning(
                    "OpenAI-compatible embedding rate limited (attempt {Attempt}/{MaxRetries}): 429, retryAfter={RetryAfterMs}ms, model={Model}",
                    attempt, maxRetries, (int)delay.TotalMilliseconds, model);

                if (attempt < maxRetries)
                {
                    response.Dispose();
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }
                break;
            }

            var isRetryable = errorBody.Contains("Model unloaded", StringComparison.OrdinalIgnoreCase)
                           || (int)response.StatusCode >= 500;

            logger.LogWarning(
                "OpenAI-compatible embedding failed (attempt {Attempt}/{MaxRetries}): {StatusCode}, model={Model}. Body: {ErrorBody}",
                attempt, maxRetries, (int)response.StatusCode, model, errorBody);

            if (!isRetryable || attempt == maxRetries)
                break;

            response.Dispose();
            logger.LogInformation("Retrying in {Delay}ms...", retryDelayMs);
            await Task.Delay(retryDelayMs, cancellationToken);
        }

        if (response == null || !response.IsSuccessStatusCode)
        {
            logger.LogError(
                "OpenAI-compatible embedding failed after {MaxRetries} attempts: {StatusCode}, model={Model}. Body: {ErrorBody}",
                maxRetries, (int)(response?.StatusCode ?? 0), model, errorBody);
            response?.Dispose();
            throw new HttpRequestException($"OpenAI-compatible embedding returned {(int)(response?.StatusCode ?? 0)}: {errorBody}");
        }

        var json = await response.Content.ReadFromJsonAsync<OpenAICompatibleEmbeddingResponse>(cancellationToken: cancellationToken);

        if (json?.Data == null || json.Data.Count == 0)
        {
            logger.LogWarning("OpenAI-compatible embedding returned no data");
            return [];
        }

        var results = new List<ReadOnlyMemory<float>>(json.Data.Count);
        foreach (var item in json.Data)
        {
            if (item.Embedding == null || item.Embedding.Length == 0)
            {
                logger.LogWarning("OpenAI-compatible embedding item has null/empty embedding (index={Index})", item.Index);
                results.Add(ReadOnlyMemory<float>.Empty);
            }
            else
            {
                results.Add(new ReadOnlyMemory<float>(item.Embedding));
            }
        }

        logger.LogInformation("Embedding success: {Count} embeddings generated", results.Count);

        return results;
    }

    /// <summary>L2 norm of a span, used by the shape check.</summary>
    private static float L2Norm(ReadOnlySpan<float> v)
    {
        float sum = 0;
        for (var i = 0; i < v.Length; i++)
            sum += v[i] * v[i];
        return MathF.Sqrt(sum);
    }

    /// <summary>
    /// Shape check: every vector must have the expected dimension and L2 norm. Runs on every
    /// response and reports once per distinct failure so a stream of bad vectors does not
    /// flood the log.
    /// </summary>
    private void ValidateShape(List<ReadOnlyMemory<float>> vectors)
    {
        if (!_validation.Enabled)
            return;

        for (var i = 0; i < vectors.Count; i++)
        {
            var v = vectors[i].Span;
            if (v.Length == 0)
                continue; // already warned above; an empty vector is reported at its source

            if (_validation.ExpectedDimension > 0 && v.Length != _validation.ExpectedDimension)
            {
                var msg = $"Embedding dimension mismatch: got {v.Length}, expected " +
                          $"{_validation.ExpectedDimension} (model={model}, endpoint={_endpoint}). " +
                          "Stored vectors are not comparable with these.";
                logger.LogError("{Message}", msg);
                if (_validation.ThrowOnShapeFailure)
                    throw new InvalidOperationException(msg);
                continue;
            }

            if (_validation.ExpectedL2Norm > 0)
            {
                var norm = L2Norm(v);
                if (MathF.Abs(norm - _validation.ExpectedL2Norm) > _validation.NormTolerance)
                {
                    var msg = $"Embedding is not L2-normalised: norm={norm:F6}, expected " +
                              $"{_validation.ExpectedL2Norm:F6} +/- {_validation.NormTolerance} " +
                              $"(model={model}, endpoint={_endpoint}). Every cosine metric assumes " +
                              "normalised vectors; scores would be silently wrong.";
                    logger.LogError("{Message}", msg);
                    if (_validation.ThrowOnShapeFailure)
                        throw new InvalidOperationException(msg);
                }
            }
        }
    }

    /// <summary>
    /// Continuity check: embed a fixed sentinel and compare it to a known-good reference for
    /// the space the stored corpus lives in. Dimension and norm can both be perfect while the
    /// space itself has changed - this is the check that notices.
    ///
    /// Three states, and the distinction between them is the whole point:
    ///
    ///   REFERENCE EXISTS  -> compare. Below threshold is a BREACH: the corpus and the server
    ///                        are in different spaces, and any mixed metric is fabricated.
    ///   NO REFERENCE,
    ///   CORPUS EMPTY      -> nothing to protect yet. Mark the capture pending; it is taken
    ///                        after the first complete round, from a node that round produced.
    ///                        Capturing here instead would be wrong - no node exists yet.
    ///   NO REFERENCE,
    ///   CORPUS NON-EMPTY  -> BREACH. A corpus exists whose space is unknown, so it can never
    ///                        be validated and re-embedding it would destroy the only record of
    ///                        that space. Report it; do NOT silently capture a live-server
    ///                        reference, which is exactly how the guard was previously defeated.
    /// </summary>
    private async Task ValidateContinuityAsync(float[] sentinelVector, CancellationToken ct)
    {
        if (_continuityChecked || sentinelVector.Length == 0)
            return;

        // A configured inline reference wins, and is the test path.
        if (_validation.SentinelReference is { Length: > 0 } inline)
        {
            Compare(inline, sentinelVector);
            return;
        }

        if (_sentinelStore is null)
        {
            _continuityChecked = true;
            WarnUnconfigured(
                "no embedding sentinel store was supplied to the embedding service");
            return;
        }

        var reference = await _sentinelStore.LoadAsync(ct);
        if (reference is not null)
        {
            // The stored text and the configured text must match, or the comparison is between
            // two different strings and the result is meaningless in both directions.
            if (reference.SentinelText is { Length: > 0 } stored &&
                !string.Equals(stored, _validation.SentinelText, StringComparison.Ordinal))
            {
                _continuityChecked = true;
                var msg =
                    "Embedding continuity check FAILED: the stored reference was captured for a " +
                    "DIFFERENT sentinel text than the one configured now. Comparing across two " +
                    "texts yields a meaningless cosine - either way it cannot detect a change of " +
                    "vector space. Re-capture the reference or restore the original SentinelText.";
                logger.LogError("{Message}", msg);
                if (_validation.ThrowOnContinuityFailure)
                    throw new InvalidOperationException(msg);
                return;
            }

            Compare(reference.Vector, sentinelVector);
            return;
        }

        // No reference. Which of the two remaining states we are in decides everything.
        _continuityChecked = true;

        // An unfinished bootstrap (marker present) is NOT a breach: the corpus may have been
        // built by this very server and simply never got its reference because the process died
        // mid-round. Resume the capture instead of crying breach.
        if (await _sentinelStore.IsCapturePendingAsync(ct))
        {
            _capturePending = true;
            logger.LogInformation(
                "Embedding continuity: no reference, but a capture is already pending from an " +
                "earlier run. Resuming; the reference will be captured after the next complete round.");
            return;
        }

        var corpusEmpty = await _sentinelStore.IsCorpusEmptyAsync(ct);

        if (corpusEmpty)
        {
            _capturePending = true;
            await _sentinelStore.MarkCapturePendingAsync(ct);
            logger.LogInformation(
                "Embedding continuity: no reference and the stored corpus is EMPTY. Nothing to " +
                "validate yet; the reference will be captured after the first complete round, " +
                "from a node that round stores. From then on it is checked on every start.");
            return;
        }

        var breach =
            "Embedding continuity BREACH: no sentinel reference exists, but the stored corpus " +
            "is NOT empty - it holds nodes with embeddings whose vector space is now unknown. " +
            "Those vectors cannot be validated against anything, so every cosine metric over " +
            "them (advance, novelty, irreducible tension) is unverified. Refusing to capture a " +
            "reference from the live server: that would compare the space against itself and " +
            "pass trivially, hiding the very change this check exists to catch. Re-embed the " +
            "corpus deliberately, or restore the reference for its original space.";
        logger.LogError("{Message}", breach);
        if (_validation.ThrowOnContinuityFailure)
            throw new InvalidOperationException(breach);
    }

    /// <summary>Compares a reference vector against the live sentinel and reports the verdict.</summary>
    private void Compare(float[] reference, float[] sentinelVector)
    {
        _continuityChecked = true;

        if (reference.Length != sentinelVector.Length)
        {
            var msg = $"Embedding continuity check FAILED: sentinel reference has {reference.Length} " +
                      $"dims but the live embedding has {sentinelVector.Length}.";
            logger.LogError("{Message}", msg);
            if (_validation.ThrowOnContinuityFailure)
                throw new InvalidOperationException(msg);
            return;
        }

        var cosine = SimdVectorUtility.CosineSimilarity(sentinelVector, reference);

        if (cosine < _validation.MinSentinelCosine)
        {
            var msg = $"Embedding continuity check FAILED: sentinel cosine={cosine:F6}, required >= " +
                      $"{_validation.MinSentinelCosine:F6} (model={model}, endpoint={_endpoint}). " +
                      "The server is producing vectors in a DIFFERENT SPACE from the stored corpus. " +
                      "Mixing stored and freshly embedded vectors will fabricate spurious advance and " +
                      "novelty - re-embed the corpus before running any metric.";
            logger.LogError("{Message}", msg);
            if (_validation.ThrowOnContinuityFailure)
                throw new InvalidOperationException(msg);
            return;
        }

        logger.LogInformation(
            "Embedding continuity check passed: sentinel cosine={Cosine:F6} (model={Model}).",
            cosine, model);
    }

    /// <summary>Reports a missing configuration once, rather than on every response.</summary>
    private void WarnUnconfigured(string reason)
    {
        if (_continuityWarned)
            return;
        _continuityWarned = true;
        logger.LogWarning(
            "Embedding continuity check SKIPPED: {Reason}. Shape is validated, but a change of " +
            "vector space would go undetected - stored embeddings would silently become " +
            "incomparable with new ones.", reason);
    }

    /// <summary>
    /// Captures the continuity reference if the corpus was empty when the service first checked
    /// and the reference is therefore still owed. Called by the ascent services after a round
    /// completes, so the reference is taken from a node the round actually stored rather than
    /// from the server alone - the reference then describes the space the CORPUS lives in, which
    /// is the only space worth validating against.
    ///
    /// A no-op when nothing is pending, so it is safe to call after every round.
    /// </summary>
    public async Task TryCaptureReferenceAsync(CancellationToken ct = default)
    {
        if (!_capturePending || _sentinelStore is null)
            return;

        // Only one round may capture; later callers see _capturePending already cleared.
        await _captureLock.WaitAsync(ct);
        try
        {
            if (!_capturePending)
                return;

            // Re-check: a reference may have appeared (another instance, or a previous round).
            if (await _sentinelStore.HasReferenceAsync(ct))
            {
                _capturePending = false;
                logger.LogInformation(
                    "Embedding continuity reference appeared before capture; using the existing one.");
                return;
            }

            var sentinel = await PostEmbeddingsAsync([_validation.SentinelText], ct);
            if (sentinel.Count == 0 || sentinel[0].Length == 0)
            {
                logger.LogWarning(
                    "Embedding continuity capture skipped: the sentinel returned an empty vector.");
                return;
            }

            await _sentinelStore.SaveAsync(_validation.SentinelText, sentinel[0].ToArray(), ct);
            _capturePending = false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Leave _capturePending set: the next round will retry rather than run unprotected
            // for the life of the process.
            logger.LogWarning(ex,
                "Embedding continuity capture failed; will retry after the next round.");
        }
        finally
        {
            _captureLock.Release();
        }
    }

    /// <summary>
    /// Single-embedding convenience overload matching the extension method signature.
    /// </summary>
    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string value,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var results = await GenerateEmbeddingsAsync([value], kernel, cancellationToken);
        return results.Count > 0 ? results[0] : ReadOnlyMemory<float>.Empty;
    }

    // -----------------------------------------------------------------------
    // IEmbeddingGenerator<string, Embedding<float>> - the MEAi interface that
    // replaces the obsolete ITextEmbeddingGenerationService on consuming sites.
    // Implemented here so a single OpenAICompatibleEmbeddingService instance satisfies
    // both DI registrations during the migration period; once all call sites are
    // updated the ITextEmbeddingGenerationService registration can be removed.
    // -----------------------------------------------------------------------

    /// <summary>
    /// MEAi entry point: delegates to <see cref="GenerateEmbeddingsAsync"/> so the
    /// validation and continuity-check path is shared with the SK interface.
    /// </summary>
    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var list = values as IList<string> ?? [.. values];
        var raw = await GenerateEmbeddingsAsync(list, kernel: null, cancellationToken);
        var embeddings = raw.Select(m => new Embedding<float>(m.ToArray())).ToList();
        return new GeneratedEmbeddings<Embedding<float>>(embeddings)
        {
            AdditionalProperties = null
        };
    }

    /// <summary>Returns metadata for the MEAi pipeline. No inner generator is wrapped.</summary>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is not null)
            return null;
        if (serviceType == typeof(EmbeddingGeneratorMetadata))
            return new EmbeddingGeneratorMetadata(nameof(OpenAICompatibleEmbeddingService), new Uri(_endpoint));
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    void IDisposable.Dispose() { /* HttpClient is owned by the caller */ }

    private class OpenAICompatibleEmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<OpenAICompatibleEmbeddingData>? Data { get; init; }

        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("usage")]
        public OpenAICompatibleEmbeddingUsage? Usage { get; init; }
    }

    private class OpenAICompatibleEmbeddingData
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }

        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("object")]
        public string? Object { get; set; }
    }

    private class OpenAICompatibleEmbeddingUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
}
