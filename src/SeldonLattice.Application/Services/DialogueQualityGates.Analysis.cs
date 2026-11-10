using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models;
using SeldonLattice.Domain.Models.Cycle;
using SeldonLattice.Domain.Options;
using SeldonLattice.Domain.Utilities;

namespace SeldonLattice.Application.Services;

public partial class DialogueQualityGates
{
    /// <summary>
    /// Calculates the ceremony score for a response based on weighted ceremonial language.
    /// </summary>
    private double CalculateCeremonyScore(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return 0;

        double score = 0;
        var lower = response.ToLowerInvariant();

        foreach (var (phrase, weight) in CeremonyWords)
        {
            var count = CountOccurrences(lower, phrase);
            score += count * weight;
        }

        return score;
    }

    /// <summary>
    /// Calculates the cosine similarity between a response embedding and the root theme
    /// embedding (cached per theme). Returns 1.0 (unquestionably on-topic) when the
    /// embedding service is unavailable so the gate degrades gracefully.
    /// </summary>
    private async Task<float> ComputeRootThemeSimilarityAsync(
        string response,
        string rootTheme,
        CancellationToken cancellationToken)
    {
        if (embeddingService is null || string.IsNullOrWhiteSpace(response) || string.IsNullOrWhiteSpace(rootTheme))
            return 1f;

        if (!_rootThemeEmbeddingCache.TryGetValue(rootTheme, out var themeEmb))
        {
            try
            {
				themeEmb = (await embeddingService.GenerateAsync([rootTheme], cancellationToken:
				cancellationToken))[0].Vector; _rootThemeEmbeddingCache.TryAdd(rootTheme, themeEmb);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "QualityGate: Failed to embed root theme; skipping relevance check.");
                return 1f;
            }
        }

        ReadOnlyMemory<float> responseEmb;
        try
        {
			responseEmb = (await embeddingService.GenerateAsync([response], cancellationToken:
			  cancellationToken))[0].Vector;
		}
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "QualityGate: Failed to embed response; skipping relevance check.");
            return 1f;
        }

        return SimdVectorUtility.CosineSimilarity(themeEmb.ToArray(), responseEmb.ToArray());
    }

    /// <summary>
    /// Calculates a simple text similarity score (0.0-1.0) between two strings.
    /// Uses Jaccard similarity on word-level n-grams (bigrams).
    /// </summary>
    public double CalculateTextSimilarity(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return 0;

        var wordsA = Tokenize(a);
        var wordsB = Tokenize(b);

        if (wordsA.Count == 0 || wordsB.Count == 0)
            return 0;

        // Generate bigrams
        var bigramsA = GetBigrams(wordsA);
        var bigramsB = GetBigrams(wordsB);

        if (bigramsA.Count == 0 || bigramsB.Count == 0)
            return 0;

        // Jaccard similarity on bigrams
        var intersection = bigramsA.Intersect(bigramsB, StringComparer.OrdinalIgnoreCase).Count();
        var union = bigramsA.Union(bigramsB, StringComparer.OrdinalIgnoreCase).Count();

        return union > 0 ? (double)intersection / union : 0;
    }

    /// <summary>
    /// Counts the number of new arguments, counterexamples, and challenging questions in a response.
    /// Uses heuristic indicators rather than semantic understanding.
    /// </summary>
    private int CountNewArguments(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return 0;

        var count = CountSubstantiveContent(response);

        var lower = response.ToLowerInvariant();
        foreach (var indicator in DisagreementIndicators)
            if (lower.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                count++;

        // If we still have zero, count any sentence that contains both a subject claim and analytical content
        if (count == 0)
        {
            var sentences = response.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var sentence in sentences)
            {
                var trimmed = sentence.Trim();
                if (trimmed.Length > 20 &&
                    (trimmed.Contains("'s") || trimmed.Contains(" is ") || trimmed.Contains(" are ") || trimmed.Contains(" represents "))
                    && !ContainsOnlyCeremonialLanguage(trimmed))
                {
                    count++;
                    if (count >= 2) break;
                }
            }
        }

        return count; // same output as before - quality scoring, agreement-stagnation detection unaffected
    }

    /// <summary>
    /// Checks if a sentence is primarily ceremonial/filler language with no substantive content.
    /// </summary>
    private static bool ContainsOnlyCeremonialLanguage(string text)
    {
        var lower = text.ToLowerInvariant();
        int ceremonialWordCount = 0;
        var words = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            foreach (var ceremony in CeremonyWords.Keys)
            {
                if (word.Contains(ceremony))
                {
                    ceremonialWordCount++;
                    break;
                }
            }
        }

        // If more than half the words are ceremonial, consider it ceremonial
        return words.Length > 0 && ceremonialWordCount >= words.Length / 2;
    }

    /// <summary>
    /// Counterexample and challenging-question signals only, independent of DisagreementIndicators.
    /// Exists so DetectEntrenchment can measure "new content" without that check being
    /// structurally guaranteed true whenever disagreement language is present.
    /// </summary>
    private static int CountSubstantiveContent(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return 0;

        var lower = response.ToLowerInvariant();
        int count = 0;

        var counterexamplePatterns = new[]
        {
            @"\bconsider\b", @"\bcounterexample\b", @"\bfor example\b",
            @"\bsuppose\b", @"\bimagine\b", @"\bwhat about\b",
            @"\bin practice\b", @"\bthe evidence shows\b", @"\bwe observe\b"
        };
        foreach (var pattern in counterexamplePatterns)
            if (Regex.IsMatch(lower, pattern, RegexOptions.IgnoreCase))
                count++;

        var questionPatterns = new[]
        {
            @"\bhow does this work when\b", @"\bwhat about\b.*\?",
            @"\bdoes this apply to\b", @"\bwhat if\b",
            @"\bwhy should\b", @"\bhow can\b.*\?"
        };
        foreach (var pattern in questionPatterns)
            if (Regex.IsMatch(lower, pattern, RegexOptions.IgnoreCase))
                count++;

        return count;
    }

    /// <summary>
    /// Computes a quality score (0-100) for a response based on the rubric:
    /// Content (40pts) + Tone (30pts) + Progression (20pts) + Length (10pts)
    /// </summary>
    private double ComputeQualityScore(
        string response,
        string? previousResponse,
        int turnCount,
        double ceremonyScore)
    {
        double score = 0;

        // Content (0-40)
        var newArgs = CountNewArguments(response);
        if (newArgs >= 3) score += 40;       // Novel argument + counterexample + new question
        else if (newArgs == 2) score += 30;  // Refines previous + adds dimension
        else if (newArgs == 1) score += 20;  // Challenges previous
        else score += 10;                    // Restates with variations

        // Tone (0-30) - relaxed thresholds to avoid over-penalizing warmth in dialogue
        if (ceremonyScore <= 3) score += 30;       // Rigorous to slightly warm
        else if (ceremonyScore <= 6) score += 25;  // Professional with moderate warmth
        else if (ceremonyScore <= 10) score += 15; // Noticeably polite but still substantive
        else if (ceremonyScore <= 15) score += 5;  // Sentimental
        // else 0 = Ceremonial only

        // Progression (0-20)
        if (previousResponse != null)
        {
            var similarity = CalculateTextSimilarity(response, previousResponse);
            if (similarity < 0.3) score += 20;       // Advances debate clearly
            else if (similarity < 0.5) score += 15;  // Builds on agreement constructively
            else if (similarity < 0.7) score += 10;  // Cycles back
            else if (similarity < 0.85) score += 5;  // Repeats previous turn
            // else 0 = No forward motion
        }
        else
        {
            score += 20; // First turn, assume progression
        }

        // Length (0-10) - brackets come from options so they can be aligned with the
        // configured dialectician reply length. See QualityGateOptions length-bracket notes:
        // misaligned brackets make this component unreachable and silently cap the score.
        var wordCount = response.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount >= options.OptimalLengthMin && wordCount <= options.OptimalLengthMax)
            score += 10;                                                       // Optimal density
        else if (wordCount > options.OptimalLengthMax && wordCount <= options.AcceptableLengthMax)
            score += 7;                                                        // Acceptable verbose
        else if (wordCount > options.AcceptableLengthMax && wordCount <= options.PaddedLengthMax)
            score += 4;                                                        // Notably padded
        else if (wordCount > options.PaddedLengthMax && wordCount <= options.BloatedLengthMax)
            score += 2;                                                        // Bloated
        // else 0 = Extreme length or below OptimalLengthMin

        return score;
    }

    /// <summary>
    /// True when the length bracket cannot be reached by a reply of the given length, which
    /// means the 10-point length component is unearnable and the score silently caps below 100.
    ///
    /// This is the "stuck at 70%" failure: the prompt asked for ~80 words while the bracket
    /// started at 400, so every reply lost the same 10 points. Detecting it explicitly is
    /// cheaper than rediscovering it from a flat score distribution.
    /// </summary>
    public bool IsLengthBracketUnreachable(int configuredTargetWords, out string reason)
    {
        if (configuredTargetWords <= 0)
        {
            reason = string.Empty;
            return false;
        }

        if (configuredTargetWords < options.OptimalLengthMin)
        {
            reason =
                $"Length bracket unreachable: dialectician replies target {configuredTargetWords} words, " +
                $"but the quality-score length bracket awards points only from {options.OptimalLengthMin} words. " +
                $"Every reply loses the full 10 length points, capping the score at 90. " +
                $"Align QualityGates:OptimalLengthMin/Max with Dialectics:DialecticianTargetWordCount..DialecticianMaxWordCount " +
                $"(or raise the word settings to meet the bracket).";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    /// <summary>
    /// Creates a quality metrics snapshot for a single response.
    /// </summary>
    public ResponseQualityMetrics AnalyzeResponse(
        string response,
        string? previousResponse,
        int turnCount,
        string? ownPreviousResponse = null)
    {
        var ceremonyScore = CalculateCeremonyScore(response);
        var similarity = previousResponse != null
            ? CalculateTextSimilarity(response, previousResponse)
            : 0.0;
        var selfSimilarity = ownPreviousResponse != null
            ? CalculateTextSimilarity(response, ownPreviousResponse)
            : 0.0;
        var newArguments = CountNewArguments(response);
        var qualityScore = ComputeQualityScore(response, previousResponse, turnCount, ceremonyScore);

        // Self-repetition penalty (deduct up to 20 pts if agent repeats itself from its previous turn)
        if (selfSimilarity >= 0.6) qualityScore = Math.Max(0, qualityScore - 20);
        else if (selfSimilarity >= 0.4) qualityScore = Math.Max(0, qualityScore - 10);

        var wordCount = response.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        return new ResponseQualityMetrics
        {
            CeremonyScore = ceremonyScore,
            SimilarityToPrevious = similarity,
            SelfSimilarity = selfSimilarity,
            NewArgumentCount = newArguments,
            QualityScore = qualityScore,
            WordCount = wordCount,
            HasFarewell = FarewellPatterns.Any(p => p.IsMatch(response)),
            HasAgreement = AgreementMarkers.Any(m =>
                response.Contains(m, StringComparison.OrdinalIgnoreCase)),
            HasDisagreement = DisagreementIndicators.Any(d =>
                response.Contains(d, StringComparison.OrdinalIgnoreCase)),
        };
    }

    private static List<string> Tokenize(string text)
    {
        return
        [
            .. text.Split([' ', '\n', '\r', '\t', ',', '.', '!', '?', ';', ':'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(w => w.Trim().ToLowerInvariant())
                .Where(w => w.Length > 2) // Skip very short words

        ];
    }

    private static HashSet<string> GetBigrams(List<string> words)
    {
        var bigrams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < words.Count - 1; i++)
        {
            bigrams.Add($"{words[i]} {words[i + 1]}");
        }
        return bigrams;
    }

    private static int CountOccurrences(string text, string phrase)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(phrase, index, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            count++;
            index += phrase.Length;
        }
        return count;
    }

}
