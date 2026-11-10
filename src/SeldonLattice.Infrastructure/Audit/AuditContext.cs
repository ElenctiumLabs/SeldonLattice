using Microsoft.Extensions.AI;

namespace SeldonLattice.Infrastructure.Audit;

/// <summary>
/// Mutable propagation carrier passed via ChatOptions.AdditionalProperties["audit_context"].
/// Links agent orchestration context (session, speaker, level, turn) to intercepted LLM calls.
/// </summary>
public class AuditContext
{
    public string SessionId { get; init; } = string.Empty;
    public string SpeakerId { get; init; } = string.Empty;
    public string ModuleKey { get; init; } = string.Empty;
    public int Level { get; init; }
    public int Turn { get; init; }
    public string? ParentCid { get; set; }
    public List<string> QualityFlags { get; } = [];
    public bool IsFlagged { get; set; }

    /// <summary>
    /// Sliding window of the last 3 completion texts for circular-reasoning detection.
    /// Capacity enforced by QualityGateDelegatingChatClient.
    /// </summary>
    public Queue<string> RecentCompletions { get; } = new(capacity: 3);

    /// <summary>
    /// Helper used by callers to build ChatOptions with this context attached.
    /// </summary>
    public ChatOptions ToOptions()
    {
        var options = new ChatOptions();
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties["audit_context"] = this;
        return options;
    }

    /// <summary>
    /// Returns a default no-op context. Used by middleware when the key is absent.
    /// </summary>
    public static AuditContext Empty { get; } = new();
}
