using SeldonLattice.Domain.Models;
using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Application.Services;

/// <summary>
/// Builds and guards the "fractal echo" boundary event.
/// <para>
/// When the ascension gate cannot generalize (the vocabulary of the current rung is
/// exhausted), the inquiry does not force another ascent on an empty theme. It returns to
/// the session's root question and re-opens it with the prior synthesis context intact.
/// This class is a pure function collection so the policy is testable without an ascent
/// service, an LLM, or a Merkle store.
/// </para>
/// </summary>
public static class FractalEcho
{
    /// <summary>SSE event type emitted at the sustained-theme boundary.</summary>
    public const string EventType = "fractal_echo";

    /// <summary>Speaker id carried by the echo event.</summary>
    public const string SpeakerId = "echo";

    /// <summary>
    /// Decides whether the boundary should emit an echo.
    /// </summary>
    /// <param name="currentTheme">The theme that was just debated.</param>
    /// <param name="rootTheme">The session's root question (the return target).</param>
    /// <param name="isSustained">Whether the ascension gate sustained the current theme.</param>
    /// <param name="alreadyEchoed">
    /// True when a prior level in this session already returned to this root. Prevents a
    /// sustained root level from echoing again and looping until the level ceiling.
    /// </param>
    public static bool ShouldEcho(
        string? currentTheme,
        string? rootTheme,
        bool isSustained,
        bool alreadyEchoed = false)
    {
        if (!isSustained || alreadyEchoed) return false;
        if (string.IsNullOrWhiteSpace(rootTheme)) return false;

        // If the inquiry is already on the root, an echo would just re-ask the same
        // question again. This is the immediate-loop guard; alreadyEchoed covers the
        // cross-level case.
        var root = rootTheme.Trim();
        var current = (currentTheme ?? string.Empty).Trim();
        return !string.Equals(current, root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates the echo event for a sustained boundary, or null when there is no usable
    /// root question.
    /// </summary>
    /// <param name="rootTheme">The session's root question.</param>
    /// <param name="level">The level being returned from.</param>
    public static DebateTurnEvent? Create(string? rootTheme, int level)
    {
        if (string.IsNullOrWhiteSpace(rootTheme)) return null;

        var root = rootTheme.Trim();
        return new DebateTurnEvent
        {
            Type = EventType,
            Turn = -1,
            Content = root,
            // The next level's theme is the root question. Carrying it here as well as in
            // Content lets the frontend chain even if it only understands the synthesis
            // event's NextTheme contract.
            NextTheme = root,
            Level = level,
            IsSustained = true,
            SpeakerId = SpeakerId,
            DialecticRole = DialecticRole.Synthesis,
            Timestamp = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Decides the next rung after a synthesis.
    /// <para>
    /// The root question is the next rung only when the echo actually fired. A sustained rung
    /// with no echo means the root has already been reopened, or the rung is the root itself;
    /// chaining anyway would re-ask the same question until the level ceiling. In that case the
    /// synthesis carries no next theme and the caller hands over instead of chaining.
    /// </para>
    /// </summary>
    /// <param name="echoEmitted">Whether the fractal echo event was actually emitted.</param>
    /// <param name="rootTheme">The session's root question.</param>
    /// <param name="ascensionNextTheme">The next theme proposed by the ascension result.</param>
    public static string ResolveNextTheme(bool echoEmitted, string? rootTheme, string? ascensionNextTheme)
    {
        if (echoEmitted && !string.IsNullOrWhiteSpace(rootTheme))
            return rootTheme;
        return string.IsNullOrWhiteSpace(ascensionNextTheme) ? string.Empty : ascensionNextTheme;
    }
}
