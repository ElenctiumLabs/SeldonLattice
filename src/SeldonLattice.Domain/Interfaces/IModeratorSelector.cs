using SeldonLattice.Domain.Models.Results;

namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Interface for dynamic moderator selection based on inquiry subject matter.
/// Returns a <see cref="ModeratorSelectionResult"/> containing the chosen
/// <see cref="IModerator"/> and a confidence score in [0.0, 1.0]. Callers that
/// only need the moderator can read <see cref="ModeratorSelectionResult"/>.Moderator;
/// synthesis/storage code that wants to persist or surface the confidence reads
/// <see cref="ModeratorSelectionResult"/>Confidence. Returning a struct-style result
/// rather than a bare <c>IModerator</c> is deliberate: the confidence is what lets
/// the module distinguish 'A won because the theme matches its strength' from
/// 'A won by default because nothing matched' - information the synthesis
/// layer needs when deciding whether to trust the moderator's frame verbatim.
/// </summary>
public interface IModeratorSelector
{
    ModeratorSelectionResult SelectModerator(string rootTheme, string currentTheme);
}