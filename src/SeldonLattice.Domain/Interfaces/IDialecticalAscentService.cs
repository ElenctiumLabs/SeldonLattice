using SeldonLattice.Domain.Configuration;
using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Service that orchestrates a dialectical ascent through multiple philosophical levels.
/// Each level involves two dialecticians debating a theme until equilibrium,
/// followed by mediation and moderator synthesis into a metaphor and next theme extraction.
/// </summary>
public interface IDialecticalAscentService
{
    /// <summary>
    /// Completes a single level of the dialectical ascent.
    /// The dialecticians debate the theme until equilibrium is detected,
    /// then the moderator synthesizes their tension into a metaphor and next theme.
    /// </summary>
    /// <param name="level">The ascent level being executed.</param>
    /// <param name="theme">The philosophical question to explore at this level.</param>
    /// <param name="sessionId">The session ID for tracking and storage.</param>
    /// <param name="config">Module-specific dialectical configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result containing synthesis hash, metaphor, and next theme.</returns>
    Task<DialogueLevelResult> AscendLevelAsync(int level, string theme, string sessionId, DialecticalConfiguration config, CancellationToken ct = default);

    /// <summary>
    /// Streaming version of the ascent that yields events for each debate turn.
    /// Useful for real-time UI updates during the dialogue process.
    /// </summary>
    /// <param name="level">The ascent level being executed.</param>
    /// <param name="theme">The philosophical question to explore at this level.</param>
    /// <param name="sessionId">The session ID for tracking and storage.</param>
    /// <param name="config">Module-specific dialectical configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Async stream of debate events.</returns>
    IAsyncEnumerable<DebateTurnEvent> AscendLevelStreamingAsync(int level, string theme, string sessionId, DialecticalConfiguration config, CancellationToken ct = default);
}
