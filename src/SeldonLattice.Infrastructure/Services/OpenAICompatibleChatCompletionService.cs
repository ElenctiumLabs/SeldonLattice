using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Domain.Options;

namespace SeldonLattice.Infrastructure.Services;

/// <summary>
/// Custom client for OpenAI-compatible chat completion endpoints.
/// Handles response variations that may be incompatible with the OpenAI SDK v2
/// deserializer (e.g., null FinishReason).
/// </summary>
public class OpenAICompatibleChatCompletionService(
    HttpClient httpClient,
    string endpoint,
    string model,
    string apiKey,
    ILogger<OpenAICompatibleChatCompletionService> logger,
    LlmOptions? llmOptions = null)
    : IChatCompletionService
{
    private readonly string _endpoint = endpoint.TrimEnd('/');
    private readonly LlmOptions _llmOptions = llmOptions ?? new LlmOptions();

    public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

    /** Sends a chat completion request to the OpenAI-compatible endpoint with retry logic for transient errors. Respects configured token limits and temperature defaults. */
    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        // Mistral's Jinja chat template only accepts "user" and "assistant" roles.
        // Sending a "system" role causes OpenAI-compatible to return 400 ("Only user and assistant roles are supported!").
        // Work-around: collect all system messages, then prepend them as a [System: …] prefix on the first user turn.
        var systemParts = chatHistory
            .Where(m => m.Role == AuthorRole.System)
            .Select(m => m.Content ?? string.Empty)
            .Where(c => c.Length > 0)
            .ToList();

        var nonSystemMessages = chatHistory
            .Where(m => m.Role != AuthorRole.System)
            .Select(m => new { role = m.Role.ToString().ToLowerInvariant(), content = m.Content ?? string.Empty })
            .ToList();

        // Build the final message list, injecting system context into the first user message.
        var messages = new List<object>();
        var systemPrefix = systemParts.Count > 0
            ? string.Join("\n\n", systemParts) + "\n\n"
            : string.Empty;

        var systemInjected = false;
        foreach (var msg in nonSystemMessages)
        {
            if (!systemInjected && msg.role == "user" && systemPrefix.Length > 0)
            {
                messages.Add(new { role = "user", content = systemPrefix + msg.content });
                systemInjected = true;
            }
            else
            {
                messages.Add(msg);
            }
        }

        // Edge case: history contains only system messages (no user turns yet).
        // Convert the combined system text into a single user message so the request is valid.
        if (messages.Count == 0 && systemPrefix.Length > 0)
        {
            messages.Add(new { role = "user", content = systemPrefix.TrimEnd() });
        }

        // Cap max_tokens at the configured limit to avoid exceeding the model's context window.
        var maxTokens = executionSettings?.ExtensionData != null && executionSettings.ExtensionData.TryGetValue("max_tokens", out var maxTok)
            ? maxTok as int? ?? _llmOptions.DefaultMaxTokens
            : _llmOptions.DefaultMaxTokens;

        // Optional structured-output constraint. Callers that need a guaranteed key set
        // (the synthesis moderators, whose negative-dialectic markers a small model
        // otherwise silently omits) pass a JSON schema through the execution settings.
        // Absent for every other call, in which case the field serialises as null and the
        // request is unchanged.
        JsonElement? responseFormat = executionSettings?.ExtensionData != null &&
                                      executionSettings.ExtensionData.TryGetValue("response_format", out var rf)
            ? rf as JsonElement?
            : null;

        var requestBody = new
        {
            model,
            messages,
            temperature = executionSettings?.ExtensionData != null && executionSettings.ExtensionData.TryGetValue("temperature", out var temp) ? temp as double? : _llmOptions.DefaultTemperature,
            top_p = executionSettings?.ExtensionData != null && executionSettings.ExtensionData.TryGetValue("top_p", out var topP) ? topP as double? : _llmOptions.TopP,
            repeat_penalty = _llmOptions.RepeatPenalty,
            max_tokens = maxTokens,
            // stream MUST be false: GetChatMessageContentsAsync deserialises the response as a
            // single JSON object. With stream:true llama.cpp returns SSE lines ("data: {...}"),
            // causing JsonException: ''d' is an invalid start of a value'.
            stream = false,
            // Structured-output constraint. When the caller supplies a JSON schema (see
            // SynthesisSchema), the server applies a GBNF grammar derived from it, so the
            // required keys cannot be omitted. A 7B model otherwise drops the
            // negative-dialectic markers even when the prompt asks for them by name.
            // Null (and therefore omitted by the serializer) for every other call.
            response_format = responseFormat,
            // Reasoning-capable templates (Gemma and others) otherwise emit a thinking channel
            // that is billed against max_tokens and returned in message.reasoning_content
            // rather than message.content. That can truncate the answer to an empty content
            // string. Sent only when thinking is disabled so servers that reject unknown
            // template kwargs see no change.
            chat_template_kwargs = _llmOptions.EnableThinking ? null : new { enable_thinking = false }
        };

        var totalContentLength = messages.Sum(m => ((dynamic)m).content?.Length ?? 0);
        var effectiveTemperature = requestBody.temperature ?? _llmOptions.DefaultTemperature;
        var effectiveTopP = requestBody.top_p ?? _llmOptions.TopP;
        logger.LogInformation(
            "OpenAI-compatible request: model={Model} messages={Count} chars={TotalChars} max_tokens={MaxTokens} temperature={Temp:F2} top_p={TopP:F2} repeat_penalty={RepeatPenalty:F2} thinking={Thinking}",
            model, messages.Count, totalContentLength, maxTokens, effectiveTemperature, effectiveTopP,
            _llmOptions.RepeatPenalty, _llmOptions.EnableThinking ? "on" : "off");

        // Retry logic for transient errors (model unloaded, 500s, 429 rate limits)
        var maxRetries = _llmOptions.MaxRetries;
        var retryDelayMs = _llmOptions.RetryDelayMs;
        var sw = Stopwatch.StartNew();
        HttpResponseMessage? response = null;
        string? errorBody = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_endpoint}/chat/completions")
            {
                Content = JsonContent.Create(requestBody)
            };
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            response = await httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
                break;

            errorBody = await response.Content.ReadAsStringAsync(cancellationToken);

            // 429 Rate Limit: respect Retry-After header with exponential backoff fallback
            if ((int)response.StatusCode == 429)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta;
                var delay = retryAfter ?? TimeSpan.FromMilliseconds(retryDelayMs * Math.Pow(2, attempt - 1));

                logger.LogWarning(
                    "OpenAI-compatible rate limited (attempt {Attempt}/{MaxRetries}): 429, retryAfter={RetryAfterMs}ms, model={Model}",
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
                "OpenAI-compatible chat completion failed (attempt {Attempt}/{MaxRetries}): {StatusCode}, model={Model}. Body: {ErrorBody}",
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
                "OpenAI-compatible chat completion failed after {MaxRetries} attempts: {StatusCode}, model={Model}, messageCount={Count}, totalContentChars={TotalChars}. Body: {ErrorBody}",
                maxRetries, (int)(response?.StatusCode ?? 0), model, messages.Count, totalContentLength, errorBody);
            response?.Dispose();
            throw new HttpRequestException($"OpenAI-compatible returned {(int)(response?.StatusCode ?? 0)}: {errorBody}");
        }

        var json = await response.Content.ReadFromJsonAsync<OpenAICompatibleChatResponse>(cancellationToken: cancellationToken);

        sw.Stop();
        IReadOnlyDictionary<string, object?>? usageMetadata = null;
        if (json?.Usage != null)
        {
            var latencyMs = sw.ElapsedMilliseconds;
            var tokensPerSec = latencyMs > 0 ? json.Usage.CompletionTokens / (latencyMs / 1000.0) : 0;
            logger.LogInformation(
                "OpenAI-compatible response: model={Model} promptTokens={PromptTokens} completionTokens={CompletionTokens} totalTokens={TotalTokens} latency={LatencyMs}ms tok/s={TokensPerSec:F1}",
                model, json.Usage.PromptTokens, json.Usage.CompletionTokens, json.Usage.TotalTokens, latencyMs, tokensPerSec);

            // Surface usage to callers (Semantic Kernel exposes this via result.Metadata).
            usageMetadata = new Dictionary<string, object?>
            {
                ["promptTokens"] = json.Usage.PromptTokens,
                ["completionTokens"] = json.Usage.CompletionTokens,
                ["totalTokens"] = json.Usage.TotalTokens,
            };
        }
        else
        {
            logger.LogInformation(
                "OpenAI-compatible response: model={Model} latency={LatencyMs}ms (no usage data)",
                model, sw.ElapsedMilliseconds);
        }

        if (json?.Choices == null || json.Choices.Count == 0)
        {
            logger.LogWarning("OpenAI-compatible chat completion returned no choices");
            return [];
        }

        var results = new List<ChatMessageContent>(json.Choices.Count);
        foreach (var choice in json.Choices)
        {
            if (choice.Message?.Content == null)
            {
                logger.LogWarning("OpenAI-compatible chat completion choice has null message content");
                continue;
            }

            // Warn when the model hit the token limit - response may be incomplete.
            if (string.Equals(choice.FinishReason, "length", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Chat response truncated at max_tokens={MaxTokens} (finish_reason=length). Consider increasing DefaultMaxTokens or reducing prompt length.",
                    maxTokens);
            }

            // Reasoning models can spend the whole budget on the thinking channel, leaving an
            // empty answer. That surfaces downstream as a JSON parse failure on empty input,
            // which is hard to trace back here - so say it plainly at the point of cause.
            var reasoningChars = choice.Message.ReasoningContent?.Length ?? 0;
            if (choice.Message.Content.Length == 0 && reasoningChars > 0)
            {
                logger.LogWarning(
                    "Chat response returned EMPTY content but {ReasoningChars} chars of reasoning_content " +
                    "(finish_reason={FinishReason}, max_tokens={MaxTokens}). The model spent its budget on " +
                    "thinking. Set HttpClient:Llm:EnableThinking=false (or raise DefaultMaxTokens).",
                    reasoningChars, choice.FinishReason, maxTokens);
            }
            else if (reasoningChars > 0)
            {
                logger.LogDebug(
                    "Chat response included {ReasoningChars} chars of reasoning_content alongside {ContentChars} chars of content.",
                    reasoningChars, choice.Message.Content.Length);
            }

            var role = choice.Message.Role switch
            {
                "assistant" => AuthorRole.Assistant,
                "user" => AuthorRole.User,
                "system" => AuthorRole.System,
                _ => AuthorRole.Assistant
            };

            // Prefer the model name from the OpenAI-compatible JSON response (authoritative);
            // fall back to the configured model name when the server omits it.
            results.Add(new ChatMessageContent(role, choice.Message.Content, modelId: json.Model ?? model, metadata: usageMetadata));
        }

        // logger.LogInformation("OpenAI-compatible chat completion success: {Count} messages generated", results.Count);
        return results;
    }

    /** Streaming implementation. Currently falls back to non-streaming GetChatMessageContentsAsync since OpenAI-compatible SSE parsing is not yet implemented. */
    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // For now, fall back to non-streaming implementation
        var messages = await GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);
        foreach (var message in messages)
        {
            yield return new StreamingChatMessageContent(message.Role, message.Content, modelId: model);
        }
    }

    private class OpenAICompatibleChatResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("object")]
        public string? Object { get; init; }

        [JsonPropertyName("created")]
        public long Created { get; init; }

        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("choices")]
        public List<OpenAICompatibleChatChoice>? Choices { get; init; }

        [JsonPropertyName("usage")]
        public OpenAICompatibleChatUsage? Usage { get; init; }
    }

    private class OpenAICompatibleChatChoice
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("message")]
        public OpenAICompatibleChatMessage? Message { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    private class OpenAICompatibleChatMessage
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        /// <summary>
        /// Separate reasoning/thinking channel emitted by reasoning-capable templates.
        /// Deserialised so it can be reported rather than silently discarded - when this is
        /// populated and <see cref="Content"/> is empty, the answer was consumed by thinking
        /// and the request needs EnableThinking=false or a larger max_tokens.
        /// </summary>
        [JsonPropertyName("reasoning_content")]
        public string? ReasoningContent { get; set; }
    }

    private class OpenAICompatibleChatUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
}
