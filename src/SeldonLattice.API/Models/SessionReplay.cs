namespace SeldonLattice.API.Models;

/// <summary>
/// Replay format for a session, suitable for re-running the debate.
/// </summary>
public sealed record SessionReplay
{
    /// <summary>The session identifier.</summary>
    public required string SessionId { get; init; }
    /// <summary>The original root question that seeded the session.</summary>
    public required string RootQuestion { get; init; }
    /// <summary>The dialectical module that produced the original ascent.</summary>
    public required string Module { get; init; }
    /// <summary>All turns in chronological order for replay.</summary>
    public required IReadOnlyList<HistoryTurn> Turns { get; init; }
    /// <summary>Total number of turns in the debate.</summary>
    public required int TotalTurns { get; init; }
}