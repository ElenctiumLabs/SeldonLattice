using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Application.Extensions;

namespace SeldonLattice.Infrastructure.Services;

/// <summary>
/// Decorator that captures every <see cref="ChatHistory"/> passed to an
/// <see cref="IChatCompletionService"/> and logs the prompt for diagnostics.
/// Wrap the real chat service with this to see exactly what the LLM receives.
///
/// When <paramref name="logConversations"/> is true (config switch
/// "Diagnostics:LogConversations"), the FULL content of every request message and
/// every raw response is written at Information level so JSON/format leaks can be
/// inspected without enabling Debug logging globally.
/// </summary>
public sealed class RecordingChatService(
    IChatCompletionService inner,
    ILogger<RecordingChatService> logger,
    bool logConversations = false) : IChatCompletionService
{
    public IReadOnlyDictionary<string, object?> Attributes => inner.Attributes;

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        LogChatHistory(chatHistory);
        var result = await inner.GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);
        if (logConversations)
        {
            LogRawResponses(result);
        }
        return result;
    }

    public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        LogChatHistory(chatHistory);
        if (logConversations)
        {
            return LogStreamingResponses(inner.GetStreamingChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken));
        }
        return inner.GetStreamingChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);
    }

    private void LogChatHistory(ChatHistory chatHistory)
    {
        var totalChars = chatHistory.Sum(m => m.Content?.Length ?? 0);
        logger.LogInformation(
            "RecordingChatService: {MessageCount} messages, {TotalChars} total chars",
            chatHistory.Count, totalChars);

        foreach (var (msg, i) in chatHistory.Select((m, i) => (m, i)))
        {
            var content = msg.Content ?? "(null)";
            if (logConversations)
            {
                logger.LogInformation(
                    "RecordingChatService: message[{Index}] role={Role}:\n{Content}",
                    i, msg.Role, content);
            }
            else
            {
                // Truncate individual messages to avoid log spam; full content is in the sum.
                var preview = content.Length > 500 ? string.Concat(content.AsSpan(0, 500), "...(truncated)") : content;
                logger.LogDebug(
                    "RecordingChatService: message[{Index}] role={Role}, length={Length}, content={Preview}",
                    i, msg.Role, content.Length, preview);
            }
        }
    }

    private void LogRawResponses(IReadOnlyList<ChatMessageContent> responses)
    {
        for (var i = 0; i < responses.Count; i++)
        {
            logger.LogInformation(
                "RecordingChatService: response[{Index}] role={Role}:\n{Content}",
                i, responses[i].Role, (responses[i].Content ?? "(null)").StripReasoningTags());
        }
    }

    private async IAsyncEnumerable<StreamingChatMessageContent> LogStreamingResponses(
        IAsyncEnumerable<StreamingChatMessageContent> stream)
    {
        var buffer = new System.Text.StringBuilder();
        var role = "assistant";
        var index = 0;
        await foreach (var chunk in stream)
        {
            if (chunk.Role is not null)
            {
                role = chunk.Role.ToString();
            }
            if (!string.IsNullOrEmpty(chunk.Content))
            {
                buffer.Append(chunk.Content);
            }
            yield return chunk;
            index++;
        }
        logger.LogInformation(
            "RecordingChatService: response[0] role={Role} ({ChunkCount} chunks):\n{Content}",
            role, index, buffer.ToString().StripReasoningTags());
    }
}
