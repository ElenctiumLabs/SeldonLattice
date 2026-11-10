using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace SeldonLattice.Application.Security;

/// <summary>
/// Prompt Injection Security Guard.
/// Scans textual content for direct/indirect injection signatures.
///
/// Note (ARCH-3): the former J-Space threat-vector scanner (ScanJSpaceVectors /
/// ComprehensiveScan / JSpaceThreatConcepts) was removed - it had no producer
/// (no code populated concept/weight vectors per turn). J-Space capture is planned
/// as a real IJSpaceService producer (see docs/jlens/); revisit threat scanning
/// only once that producer exists.
/// </summary>
public class PromptInjectionSecurityGuard(ILogger<PromptInjectionSecurityGuard>? logger = null)
{
    // Pattern regexes for direct & indirect prompt injection signatures
    private static readonly Regex[] InjectionRegexes =
    [
        new(@"\bignore\s+.*?\b(instructions|prompts|rules|guidelines|directives)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bdisregard\s+.*?\b(instructions|prompts|rules|guidelines|directives)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\b(system\s+prompt|system_prompt)\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\b(print|show|reveal|display|output)\s+.*?\b(system\s+prompt|initial\s+instructions)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\byou\s+are\s+now\s+in\s+.*?\b(developer|dan|jailbreak|unrestricted)\s+mode\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bnew\s+instruction\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\b(override|bypass)\s+.*?\b(filters|guardrails|protocols|rules)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\<\s*\/\s*untrusted_input_context\s*\>", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    ];

    /// <summary>
    /// Scans text for direct/indirect prompt injection patterns.
    /// </summary>
    public SecurityScanResult ScanText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return SecurityScanResult.Clean;

        var matched = new List<string>();

        foreach (var regex in InjectionRegexes)
        {
            if (regex.IsMatch(text))
            {
                matched.Add(regex.ToString());
            }
        }

        if (matched.Count > 0)
        {
            logger?.LogWarning("SecurityGuard: Prompt injection pattern detected! Matched count: {Count}", matched.Count);

            return new SecurityScanResult
            {
                IsThreatDetected = true,
                ThreatType = "Text_Prompt_Injection_Signature",
                Details = $"Detected {matched.Count} prompt injection pattern(s) in input text.",
                MatchedPatterns = [.. matched]
            };
        }

        return SecurityScanResult.Clean;
    }
}
