using MessagePack;

namespace SeldonLattice.Domain.Models.Storage;

/// <summary>
/// Represents a dialectical dialogue session tracking the full ascent journey.
/// Owned by a single user and scoped to a single dialectical framework (module).
///
/// Session metadata is persisted in SQLite (single source of truth); the dialectical
/// nodes produced by the ascent remain in the Merkle DAG for tamper-evident auditing.
/// MessagePack formattability keeps the type usable as a generic Merkle-store payload
/// where needed (e.g. backup/export round-trips).
/// </summary>
[MessagePackObject]
public sealed class Session
{
    /// <summary>Unique identifier for this session.</summary>
    [Key(0)]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// The dialectical framework (module) that produced this session:
    /// "philosophy" | "research" | a runtime-created custom module key.
    /// </summary>
    [Key(1)]
    public string Framework { get; set; } = string.Empty;

    /// <summary>The initial philosophical question that seeds the ascent.</summary>
    [Key(2)]
    public string RootQuestion { get; set; } = string.Empty;

    /// <summary>
    /// Identity of the owning user. Resolved from the API key now; maps to the
    /// SSO provider subject once authentication is wired in.
    /// </summary>
    [Key(3)]
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>Lifecycle state. Archived sessions are retained for audit but hidden from active lists.</summary>
    [Key(4)]
    public SessionStatus Status { get; set; } = SessionStatus.Active;

    /// <summary>When this session was created.</summary>
    [Key(5)]
    public DateTime CreatedAt { get; set; }

    /// <summary>Last time this session was updated (new level completed, etc.).</summary>
    [Key(6)]
    public DateTime? LastUpdatedAt { get; set; }

    /// <summary>Total count of dialogue nodes across all levels.</summary>
    [Key(7)]
    public int NodeCount { get; set; }

    /// <summary>The highest ascent level reached in this session.</summary>
    [Key(8)]
    public int MaxLevel { get; set; }

    /// <summary>True when the session reached a sustained/hard-stop state (ascent impossible in this vocabulary). Blocks repetition.</summary>
    [Key(9)]
    public bool IsHardStopped { get; set; } = false;
}