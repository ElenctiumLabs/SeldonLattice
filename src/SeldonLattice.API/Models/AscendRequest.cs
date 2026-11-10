namespace SeldonLattice.API.Models;

/// <summary>
/// Request body for triggering a dialectical ascent.
/// </summary>
/// <param name="Level">The ascent level to execute.</param>
/// <param name="Theme">The theme/topic to explore in this ascent level.</param>
/// <param name="SessionId">Optional existing session ID. If omitted, a new session is created.</param>
/// <param name="Module">The module key (e.g. "philosophy", "research", "custom").</param>
/// <param name="RootTheme">Optional root question for domain anchoring. If not provided, uses the session's root question or the theme itself.</param>
public sealed record AscendRequest(int Level, string Theme, string? SessionId = null, string Module = "philosophy", string? RootTheme = null);