using System.Text;

namespace SeldonLattice.Application.Memory;

/// <summary>
/// Formats recalled claims into the grounding block that is folded into a level's
/// opening user turn when cross-session memory is enabled. Deterministic and bounded so
/// it is unit-testable and cannot blow up the prompt: claim text is trimmed to a hard
/// ceiling, ≤ <paramref name="maxClaims"/> claims are emitted, and provenance
/// (which prior session adjudicated the claim, at what level) is included so the model
/// can distinguish "established by a prior session" from its own reasoning.
/// </summary>
public static class ClaimMemoryPromptBuilder
{
    public const int MaxClaimCharacters = 320;

    /// <summary>
    /// Returns the grounded-memory block, or null when there is nothing worth recalling.
    /// The block is wrapped in the same "private scaffolding, for your eyes only" framing
    /// the rest of the debate directives use, so it is never echoed verbatim.
    /// </summary>
    public static string? Build(IReadOnlyList<RetrievedClaim> claims, int maxClaims)
    {
        if (claims.Count == 0 || maxClaims <= 0)
            return null;

        var sb = new StringBuilder();
        sb.Append(
            "[PRIOR ADJUDICATED POSITIONS - claims from earlier sessions on this root question. " +
            "Treat them as provisional ground to engage, extend, or refute; do not echo them " +
            "verbatim, and do not treat confidence as certainty. This is private scaffolding " +
            "and must never be repeated in your reply.]");
        sb.Append('\n');

        var count = 0;
        foreach (var claim in claims)
        {
            if (count >= maxClaims) break;
            var text = claim.Claim;
            if (text.Length > MaxClaimCharacters)
                text = text.Substring(0, MaxClaimCharacters).TrimEnd() + "…";

            sb.Append("• ");
            sb.Append(text);
            sb.Append(" (prior session at level ");
            sb.Append(claim.Level);
            sb.Append(", confidence ");
            sb.Append(Math.Round(claim.Confidence * 100));
            sb.Append('%');
            if (!string.IsNullOrWhiteSpace(claim.FalsifiedBy))
            {
                sb.Append(", falsified by: ");
                var falsified = claim.FalsifiedBy;
                if (falsified.Length > 140) falsified = falsified.Substring(0, 140).TrimEnd() + "…";
                sb.Append(falsified);
            }
            sb.Append(')');
            sb.Append('\n');
            count++;
        }

        return sb.ToString().TrimEnd();
    }
}