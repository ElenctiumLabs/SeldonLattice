namespace SeldonLattice.API.Models;

/// <summary>
/// Full debate history for a session, ordered chronologically.
/// </summary>
public sealed record SessionHistory
{
    /// <summary>The session these turns belong to.</summary>
    public required string SessionId { get; init; }
    /// <summary>The original root question that seeded the session.</summary>
    public required string RootQuestion { get; init; }
    /// <summary>All turns in the debate, ordered chronologically.</summary>
    public required IReadOnlyList<HistoryTurn> Turns { get; init; }
}