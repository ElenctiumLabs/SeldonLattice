using MessagePack;
using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.Domain.Models.Storage;

/// <summary>
/// Immutable Merkle DAG node that captures a single LLM completion made by any agent.
/// Written by <c>AuditDelegatingChatClient</c> for every intercepted call across all modules.
/// Quality flags and post-quantum signatures provide cryptographic audit guarantees.
/// </summary>
[MessagePackObject]
public record LlmAuditNode : IMerkleNode
{
    /// <summary>Fixed type discriminator. Always "llm_audit".</summary>
    [Key(0)]  public string   NodeType         { get; init; } = "llm_audit";

    /// <summary>Session this call belongs to.</summary>
    [Key(1)]  public string   SessionId        { get; init; } = string.Empty;

    /// <summary>Agent identity, e.g. "mediator", "system".</summary>
    [Key(2)]  public string   SpeakerId        { get; init; } = string.Empty;

    /// <summary>Module identifier, e.g. "philosophy", "research".</summary>
    [Key(3)]  public string   ModuleKey        { get; init; } = string.Empty;

    /// <summary>Dialectical ascent level (0-99).</summary>
    [Key(4)]  public int      Level            { get; init; }

    /// <summary>Turn number within the level (0-999).</summary>
    [Key(5)]  public int      Turn             { get; init; }

    /// <summary>
    /// Role-prefixed 120-character summaries of each input message,
    /// e.g. "system: &lt;first 120 chars&gt;...". Not raw content - avoids duplicating prompt data.
    /// </summary>
    [Key(6)]  public string[] PromptMessages   { get; init; } = [];

    /// <summary>Full completion text returned by the LLM.</summary>
    [Key(7)]  public string   CompletionText   { get; init; } = string.Empty;

    /// <summary>Token count for the input messages (0 when usage is unavailable).</summary>
    [Key(8)]  public int      InputTokenCount  { get; init; }

    /// <summary>Token count for the completion (0 when usage is unavailable).</summary>
    [Key(9)]  public int      OutputTokenCount { get; init; }

    /// <summary>Model identifier from the completion response.</summary>
    [Key(10)] public string   ModelId          { get; init; } = string.Empty;

    /// <summary>Wall-clock milliseconds from before the inner LLM call to after it returned.</summary>
    [Key(11)] public long     LatencyMs        { get; init; }

    /// <summary>UTC timestamp set at interception time.</summary>
    [Key(12)] public DateTime Timestamp        { get; init; }

    /// <summary>CID of the DialecticalNode that triggered this call, when available.</summary>
    [Key(13)] public string?  ParentCid        { get; init; }

    /// <summary>
    /// Advisory quality violation codes, e.g. "circular_reasoning", "ceremonial_language",
    /// or "llm_error:&lt;ExceptionType&gt;" on inner-client failure.
    /// </summary>
    [Key(14)] public string[] QualityFlags     { get; init; } = [];

    /// <summary>True when any quality flag is set or the inner LLM call threw.</summary>
    [Key(15)] public bool     IsFlagged        { get; init; }

    /// <summary>Computes the content identifier by hashing the MessagePack-serialised bytes.</summary>
    public Cid    GetCid()    => Cid.Compute(Serialize());

    /// <summary>Serialises this node using MessagePack for storage.</summary>
    public byte[] Serialize() => MessagePackSerializer.Serialize(this);
}
