namespace SeldonLattice.Domain.Models.Cycle;

/// <summary>
/// Verdict from the ascension gate evaluating whether a candidate theme generalizes from the current level.
/// </summary>
public record AscensionVerdict(bool IsValidGeneralization, string Reasoning);