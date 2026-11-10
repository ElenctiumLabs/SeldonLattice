using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace SeldonLattice.Application.Tests;

/// <summary>
/// Canned-response chat service for moderator/gate tests. Maps a substring of the first
/// system prompt to a canned reply (longest match wins); captures every prompt seen so
/// tests can assert contract injection. Unknown prompts get a minimal synthesis JSON.
/// </summary>
internal sealed class CapturingChatCompletionService : IChatCompletionService
{
    public ConcurrentDictionary<string, string> Responses { get; } = new();

    public List<string> PromptsSeen { get; } = [];

    public IReadOnlyDictionary<string, object?> Attributes { get; } =
        new Dictionary<string, object?>();

    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null, CancellationToken cancellationToken = default)
    {
        var systemPrompt = string.Join("\n", chatHistory.Select(m => m.Content ?? string.Empty));
        PromptsSeen.Add(systemPrompt);

        var json = PickResponse(systemPrompt);
        var msg = new ChatMessageContent(AuthorRole.Assistant, json);
        return Task.FromResult<IReadOnlyList<ChatMessageContent>>([msg]);
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var systemPrompt = string.Join("\n", chatHistory.Select(m => m.Content ?? string.Empty));
        PromptsSeen.Add(systemPrompt);
        var json = PickResponse(systemPrompt);
        await Task.Yield();
        yield return new StreamingChatMessageContent(AuthorRole.Assistant, json);
    }

    private string PickResponse(string prompt)
    {
        foreach (var (key, value) in Responses.OrderByDescending(kv => kv.Key.Length))
        {
            if (prompt.Contains(key, StringComparison.OrdinalIgnoreCase))
                return value;
        }
        return DefaultSynthesisJson;
    }

    public const string DefaultSynthesisJson = """
        {
          "image_domain": "probability",
          "metaphor": "The market is a martingale: each fair game unfolds without drift.",
          "thesis": "Thesis from stub service.",
          "next_theme": "Does the martingale assumption hold across regimes?",
          "connective_sentence": "This generalizes the root because it asks whether stochastic fairness is global.",
          "claims": [
            {
              "claim": "A fair game has no memory of its history.",
              "support_from": ["turn-3"],
              "attacked_by": ["turn-2"],
              "falsified_by": "A regime where increments are serially dependent and prices trend.",
              "confidence": 0.7
            }
          ],
          "remaining_dissensus": "Whether real markets ever satisfy the independence assumption.",
          "self_check": { "repeats_premise": 0.1, "grounded": true }
        }
        """;
}