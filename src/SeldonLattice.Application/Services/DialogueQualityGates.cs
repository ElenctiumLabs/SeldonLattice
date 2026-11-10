using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.AI;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models;
using SeldonLattice.Domain.Models.Cycle;
using SeldonLattice.Domain.Options;
using SeldonLattice.Domain.Utilities;

namespace SeldonLattice.Application.Services;

/// <summary>
/// Implements quality gates for agent dialogue to prevent "zombie dialogue" problems.
/// Detects ceremonial language escalation, content repetition, agreement stagnation,
/// excessive farewell loops, and topic drift away from the root question.
/// Provides both hard termination triggers and soft warnings.
/// </summary>
public partial class DialogueQualityGates(
    QualityGateOptions options,
    ILogger<DialogueQualityGates> logger,
    ISentinelSecurityScanner? securityScanner = null,
    IEmbeddingGenerator<string, Embedding<float>>? embeddingService = null)
{
    private int _missingEmbeddingWarningLogged;

    /// <summary>
    /// Caches the root-theme embedding per theme so the relevance check only embeds
    /// the theme once per dialogue instead of once per turn.
    /// </summary>
    private readonly ConcurrentDictionary<string, ReadOnlyMemory<float>> _rootThemeEmbeddingCache = new();
    /// <summary>
    /// Weighted ceremony words and their scores, from the YAML config spec.
    /// Higher weight = more indicative of ceremonial filler.
    /// Reduced weights to avoid over-penalizing natural warmth in dialogue.
    /// </summary>
    private static readonly Dictionary<string, double> CeremonyWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dear friend"] = 1.5,
        ["deeply moved"] = 1.0,
        ["profound"] = 1.0,
        ["eternal"] = 1.0,
        ["sacred"] = 1.0,
        ["beloved"] = 1.5,
        ["cherish"] = 1.0,
        ["gratitude"] = 0.75,
        ["honor"] = 0.5,
        ["heartfelt"] = 0.75,
        ["boundless"] = 0.5,
        ["illuminated"] = 0.5,
        ["whisper"] = 0.5,
        ["echo"] = 0.5,
    };

    /// <summary>
    /// Farewell indicator phrases that trigger exit detection.
    /// </summary>
    private static readonly Regex[] FarewellPatterns =
    [
        new(@"\bfarewell\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bgoodbye\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bthe end\b(?!uring|owed|orsed|eavor)", RegexOptions.IgnoreCase | RegexOptions.Compiled), // belt-and-suspenders even with \b
        new(@"\badieu\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bthis concludes\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\bour dialogue ends\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    /// <summary>
    /// Agreement marker phrases used for stagnation detection.
    /// </summary>
    private static readonly string[] AgreementMarkers =
        ["i concur", "you make a fair point", "that's well-argued", "i agree", "well said", "excellent point"];

    /// <summary>
    /// Disagreement indicator phrases that count as substantive content.
    /// </summary>
    private static readonly string[] DisagreementIndicators =
        ["but", "however", "yet", "contradicts", "however, consider", "but wait", "counterexample"];

    /// <summary>
    /// Detects a bare abstention reply: either exactly
    /// "[ABSTAIN]" or "[ABSTAIN]" followed by a short reason. Length-bounded so a normal
    /// response that merely opens with the token is NOT treated as an abstention - forcing
    /// confident-prose models to actually decline instead of "borrowing" the token.
    /// </summary>
    public static bool IsAbstention(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        const int maxAbstentionLength = 200;
        var trimmed = text.Trim();

        if (trimmed.Length == 9 && trimmed.Equals("[ABSTAIN]", StringComparison.OrdinalIgnoreCase))
            return true;

        return trimmed.StartsWith("[ABSTAIN]", StringComparison.OrdinalIgnoreCase)
            && trimmed.Length <= maxAbstentionLength
            && trimmed[9] is ':' or ' ';
    }

    /// <summary>
    /// Exposes the quality score exit threshold for external use (e.g., by the streaming service).
    /// </summary>
    public double QualityScoreExitThreshold => options.QualityScoreExitThreshold;

    /// <summary>
    /// Exposes whether quality gate checking is enabled.
    /// </summary>
    public bool Enabled => options.Enabled;

    /// <summary>
    /// Exposes the number of sustained turns above the premise-fork distance threshold
    /// needed to declare a deep premise-level fork.
    /// </summary>
    public int PremiseForkSustainedTurns => options.PremiseForkSustainedTurns;

    /// <summary>
    /// Evaluates the current dialogue state and determines if termination is required.
    /// Returns null if dialogue should continue, or an exit reason if termination is needed.
    /// Also logs soft warnings for quality degradation.
    /// </summary>
    public async Task<DialogueExitReason?> EvaluateAndLogAsync(
        DialogueQualityState state,
        string currentResponse,
        string? previousResponse,
        int turnCount,
        string? rootTheme = null,
        string? module = null,
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
            return null;

        // --- Hard stop checks ---

        // 0. Prompt Injection / Security Guard check
        // ARCH-3: ScanText only - J-Space threat-vector scanning (ComprehensiveScan)
        // was removed as it had no producer; revisit once an IJSpaceService exists.
        // When Sentinel is configured, uses ISentinelSecurityScanner; otherwise falls back to local guard.
        var securityResult = securityScanner is not null
            ? await securityScanner.ScanTextAsync(currentResponse, cancellationToken: cancellationToken)
            : LocalScanToResult(new Security.PromptInjectionSecurityGuard().ScanText(currentResponse));
        if (securityResult.IsThreatDetected)
        {
            logger.LogWarning("QualityGate: Prompt Injection signature detected in response! Forcing security exit. Details: {Details}", securityResult.Details);
            return DialogueExitReason.PromptInjectionDetected;
        }

        // 1. Max turns exceeded
        if (turnCount >= options.MaxTurns)
        {
            logger.LogWarning("QualityGate: Max turns ({MaxTurns}) exceeded. Forcing exit.", options.MaxTurns);
            return DialogueExitReason.MaxTurnsExceeded;
        }

        // Abstention: a bare [ABSTAIN] reply is a valid,
        // low-severity turn. No penalty, and it resets the off-topic ladder so a healthy
        // "I cannot ground this yet" is never counted as drift. The dialogue loop hands
        // the next turn to the other dialectician, giving the abstained agent room to
        // return with grounding instead of forcing confident prose.
        if (IsAbstention(currentResponse))
        {
            state.ConsecutiveOffTopicTurns = 0;
            logger.LogInformation("QualityGate: Turn {Turn} returned [ABSTAIN]; continuing without penalty.", turnCount);
            return null;
        }

        // 1a. Off-theme drift: response semantically far from the root question for
        // consecutive turns. Catches debates that become a competent dialogue about the
        // wrong subject (topic drift) while every other gate passes.
        if (rootTheme is not null && options.RelevanceGateEnabled &&
            turnCount >= options.MinTurnsForRelevanceCheck && embeddingService is null &&
            System.Threading.Interlocked.Exchange(ref _missingEmbeddingWarningLogged, 1) == 0)
        {
            logger.LogWarning(
                "Relevance gate is enabled but no embedding service is available; root-theme relevance checks are being skipped.");
        }

        if (rootTheme is not null && options.RelevanceGateEnabled &&
            turnCount >= options.MinTurnsForRelevanceCheck && embeddingService is not null)
        {
            var relevance = await ComputeRootThemeSimilarityAsync(currentResponse, rootTheme, cancellationToken);
            if (relevance < options.OnThemeRelevanceThreshold)
            {
                state.ConsecutiveOffTopicTurns++;
                logger.LogWarning(
                    "QualityGate [SOFT]: Response diverges from root theme (similarity {Sim:F2} < threshold {Threshold:F2}, turn {Turn}).",
                    relevance, options.OnThemeRelevanceThreshold, turnCount);
                if (state.ConsecutiveOffTopicTurns >= options.RelevanceConsecutiveTurns)
                {
                    logger.LogWarning(
                        "QualityGate: Response off the root theme for {Count} consecutive turns (similarity {Sim:F2}). Forcing exit.",
                        state.ConsecutiveOffTopicTurns, relevance);
                    return DialogueExitReason.OffTopicDrift;
                }
            }
            else
            {
                state.ConsecutiveOffTopicTurns = 0;
            }
        }

        // 2. Consecutive farewells
        if (DetectConsecutiveFarewells(state.ResponseHistory, options.ConsecutiveFarewellsThreshold))
        {
            logger.LogWarning("QualityGate: {Count} consecutive farewells detected. Forcing exit.",
                options.ConsecutiveFarewellsThreshold);
            return DialogueExitReason.EndlessGoodbye;
        }

        // 3. Content repetition (similarity to previous response)
        // Skip on early turns - agents are establishing their voices, especially
        // with smaller models that naturally produce similar responses.
        if (previousResponse != null && turnCount >= options.MinTurnsForSimilarityCheck)
        {
            var similarity = CalculateTextSimilarity(currentResponse, previousResponse);
            if (similarity >= options.SimilarityThreshold)
            {
                logger.LogWarning("QualityGate: Response similarity {Sim:F2} exceeds threshold {Thresh:F2} at turn {Turn}. Forcing exit.",
                    similarity, options.SimilarityThreshold, turnCount);
                return DialogueExitReason.RepetitiveContent;
            }
        }

        // 4. Agreement stagnation
        if (DetectAgreementStagnation(state, options.AgreementStagnationTurns))
        {
            logger.LogWarning("QualityGate: Agreement stagnation for {Turns} turns with no new ideas. Forcing exit.",
                options.AgreementStagnationTurns);
            return DialogueExitReason.AgreementWithoutTension;
        }

        // 5. Low quality score for consecutive turns
        if (state.ConsecutiveLowQualityTurns >= options.LowQualityConsecutiveTurns)
        {
            logger.LogWarning("QualityGate: {Count} consecutive low-quality turns. Forcing exit.",
                state.ConsecutiveLowQualityTurns);
            return DialogueExitReason.QualityScoreDegraded;
        }

        // --- Soft warnings (log but continue) ---

        // Ceremony score
        var ceremonyScore = CalculateCeremonyScore(currentResponse);
        if (ceremonyScore > options.CeremonyScoreWarning)
        {
            logger.LogWarning("QualityGate [SOFT]: Ceremony score {Score:F1} exceeds warning threshold {Thresh:F1}.",
                ceremonyScore, options.CeremonyScoreWarning);
        }

        // Verbose response (word count, not character length)
        var wordCount = currentResponse.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount > options.VerboseWordCount)
        {
            logger.LogWarning("QualityGate [SOFT]: Response word count {Count} exceeds verbose threshold {Thresh}.",
                wordCount, options.VerboseWordCount);
        }

        // Content repetition (soft)
        if (previousResponse != null && turnCount >= options.MinTurnsForSimilarityCheck)
        {
            var softSimilarity = CalculateTextSimilarity(currentResponse, previousResponse);
            if (softSimilarity >= options.ContentRepetitionWarning && softSimilarity < options.SimilarityThreshold)
            {
                logger.LogWarning("QualityGate [SOFT]: Response similarity {Sim:F2} exceeds soft warning threshold {Thresh:F2}.",
                    softSimilarity, options.ContentRepetitionWarning);
            }
        }

        // No new ideas
        var newIdeaCount = CountNewArguments(currentResponse);
        if (newIdeaCount == 0)
        {
            logger.LogWarning("QualityGate [SOFT]: No new arguments detected in this response.");
        }

        return null; // Continue dialogue
    }

    /// <summary>
    /// Detects if the last N responses in history contain farewell indicators.
    /// </summary>
    private static SentinelSecurityScanResult LocalScanToResult(Security.SecurityScanResult local) =>
        local.IsThreatDetected
            ? new SentinelSecurityScanResult
            {
                IsThreatDetected = true,
                ThreatType = local.ThreatType,
                Details = local.Details,
                MatchedPatterns = local.MatchedPatterns
            }
            : SentinelSecurityScanResult.Clean;

    private bool DetectConsecutiveFarewells(List<string> history, int count)
    {
        if (history.Count < count)
            return false;

        int consecutiveCount = 0;
        for (int i = history.Count - 1; i >= 0; i--)
        {
            var lower = history[i].ToLowerInvariant();
            bool isFarewell = FarewellPatterns.Any(p => p.IsMatch(lower));

            if (isFarewell)
                consecutiveCount++;
            else
                break; // Interrupted sequence

            if (consecutiveCount >= count)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Detects if the dialogue has stagnated: both agents agreeing for N+ turns with no new ideas.
    /// </summary>
    private bool DetectAgreementStagnation(DialogueQualityState state, int turnThreshold)
    {
        var history = state.ResponseHistory;
        if (history.Count < turnThreshold + 2)
            return false;

        // Check last N turns for constant agreement
        bool allAgreement = true;
        for (int i = history.Count - turnThreshold; i < history.Count; i++)
        {
            if (!ContainsAgreementMarkers(history[i]))
            {
                allAgreement = false;
                break;
            }
        }

        if (!allAgreement)
            return false;

        // Check if any new ideas introduced during agreement period
        int newIdeasCount = 0;
        for (int i = history.Count - turnThreshold; i < history.Count; i++)
        {
            newIdeasCount += CountNewArguments(history[i]);
        }

        // If agreeing but no new ideas, stagnation detected
        return newIdeasCount == 0;
    }

}
