namespace SeldonLattice.Domain.Models.Results;

/// <summary>
/// Result of synthesizing the next dialectical theme from the current level's conclusion.
/// </summary>
/// <param name="Theme">The proposed next rung, or empty when the moderator declined.</param>
/// <param name="ConnectiveSentence">The proposed rung's claimed connection to the root question.</param>
/// <param name="IsExhausted">
/// True when the moderator declared the current rung spent rather than proposing a next question.
/// The ascension gate answers for that claim before it is acted on.
/// </param>
/// <param name="ExhaustionReason">The moderator's reason for declining, when it gave one.</param>
public record NextThemeResult(
    string Theme,
    string ConnectiveSentence,
    bool IsExhausted = false,
    string? ExhaustionReason = null);