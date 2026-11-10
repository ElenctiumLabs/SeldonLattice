namespace SeldonLattice.Domain.Models.Results;

/// <summary>
/// Result of a moderation synthesis: the metaphor itself, the image domain it was drawn from,
/// and the actual philosophical claim being synthesized (to ensure output isn't just atmospheric poetry).
/// </summary>
/// <summary>
/// Result of a moderation synthesis: the metaphor itself, the image domain it was drawn from,
/// and the actual philosophical claim being synthesized (to ensure output isn't just atmospheric poetry).
/// </summary>
public record MetaphorResult(
    string Metaphor,
    string ImageDomain,
    string Thesis,
    string? NextTheme = null,
    string? ConnectiveSentence = null,
    IReadOnlyList<ClaimDraft>? Claims = null,
    string? RemainingDissensus = null,
    float? RepeatsPremise = null,
    float? JoinMediation = null,
    float? JoinNovelty = null);
