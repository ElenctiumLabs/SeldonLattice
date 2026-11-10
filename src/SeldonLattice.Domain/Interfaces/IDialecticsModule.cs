using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Interface for a dialectics module.
/// Each module (philosophy, research, custom, etc.) implements this interface
/// and registers itself with the IDialecticsModuleRegistry for pluggable architecture.
/// </summary>
public interface IDialecticsModule
{
    /// <summary>
    /// Unique identifier for this module (e.g., "philosophy", "research").
    /// </summary>
    string Key { get; }

    /// <summary>
    /// Human-readable name for UI display.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Description of what this module does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Executes a single level of dialectical ascent within this module's framework.
    /// </summary>
    /// <param name="level">The ascent level.</param>
    /// <param name="theme">The theme to explore.</param>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="rootTheme">The root question for domain anchoring.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the ascent level.</returns>
    Task<DialogueLevelResult> ExecuteLevelAsync(
        int level,
        string theme,
        string sessionId,
        string rootTheme,
        CancellationToken ct = default
    );

    /// <summary>
    /// Streaming version of ExecuteLevelAsync for real-time UI updates.
    /// </summary>
    IAsyncEnumerable<DebateTurnEvent> ExecuteLevelStreamingAsync(
        int level,
        string theme,
        string sessionId,
        string rootTheme,
        CancellationToken ct = default
    );
}