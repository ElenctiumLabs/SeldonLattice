using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace SeldonLattice.Application.Extensions;

/// <summary>
/// Extension methods for <see cref="ChatHistory"/> used by mediators to format
/// conversation history into prompt-friendly text.
/// </summary>
public static class ChatHistoryExtensions
{
    /// <summary>
    /// Formats a ChatHistory into a readable summary for mediator prompts.
    /// Skips the system prompt (index 0), labels each turn as [Opponent] or [Self],
    /// and truncates long messages to <paramref name="maxMessageLength"/> characters.
    /// </summary>
    public static string FormatForMediator(this ChatHistory history, int maxMessageLength = 300)
    {
        if (history.Count <= 1) return "(no prior turns)";

        var lines = new List<string>();
        for (var i = 1; i < history.Count; i++)
        {
            var msg = history[i];
            var role = msg.Role == AuthorRole.User ? "Opponent" : "Self";
            var content = msg.Content ?? string.Empty;
            if (content.Length > maxMessageLength)
                content = content[..maxMessageLength] + "...";
            lines.Add($"[{role}]: {content}");
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Guarantees the history contains at least one non-system message.
    /// Some connectors (e.g. Google Gemini) reject histories that contain only system
    /// messages with a validation error. When the history is system-only, the first system
    /// message is demoted to a user message - no tokens are added or removed, so role
    /// and instruction semantics are preserved for tolerant providers (including llama.cpp).
    /// </summary>
    public static ChatHistory EnsureAtLeastOneUserMessage(this ChatHistory history)
    {
        if (history.Any(m => m.Role != AuthorRole.System))
            return history;

        var fixedHistory = new ChatHistory();
        for (var i = 0; i < history.Count; i++)
        {
            var message = history[i];
            fixedHistory.Add(i == 0
                ? new ChatMessageContent(AuthorRole.User, message.Content ?? string.Empty)
                : message);
        }
        return fixedHistory;
    }
}
