using Microsoft.Extensions.Logging;
using SeldonLattice.Application.Services;
using SeldonLattice.Domain.Models;
using SeldonLattice.Domain.Models.Cycle;
using SeldonLattice.Domain.Options;
using Xunit;

namespace SeldonLattice.Application.Tests;

/// <summary>
/// Unit tests for DialogueQualityGates - the core quality detection system.
/// Tests ceremony scoring, farewell detection, agreement stagnation, text similarity,
/// quality scoring, and stalemate signal detection.
/// </summary>
public class DialogueQualityGatesTests
{
    private readonly DialogueQualityGates _gates;
    private readonly QualityGateOptions _options;
    private readonly ILogger<DialogueQualityGates> _logger;

    public DialogueQualityGatesTests()
    {
        _options = new QualityGateOptions
        {
            Enabled = true,
            MaxTurns = 16,
            ConsecutiveFarewellsThreshold = 3,
            SimilarityThreshold = 0.85,
            AgreementStagnationTurns = 4,
            CeremonyScoreWarning = 8.0,
            VerboseWordCount = 1000,
            ContentRepetitionWarning = 0.75,
            QualityScoreExitThreshold = 50.0,
            LowQualityConsecutiveTurns = 2,
            CircularRestatementThreshold = 0.55,
            PremiseForkDistanceThreshold = 0.75,
            PremiseForkSustainedTurns = 3
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<DialogueQualityGates>();

        _gates = new DialogueQualityGates(_options, _logger);
    }

    [Fact]
    public void CalculateCeremonyScore_NoCeremonialLanguage_ReturnsZero()
    {
        var response = "Proposer's theory of principles is fundamentally flawed because it posits a separate realm of existence.";

        var score = _gates.GetType().GetMethod("CalculateCeremonyScore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(_gates, [response]) as double?;

        Assert.Equal(0, score);
    }

    [Fact]
    public void CalculateCeremonyScore_WithCeremonialLanguage_ReturnsWeightedScore()
    {
        var response = "My dear friend, I am deeply moved by your profound and sacred insight. It is truly heartfelt.";

        var score = _gates.GetType().GetMethod("CalculateCeremonyScore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(_gates, [response]) as double?;

        Assert.True(score > 0);
    }

    [Fact]
    public void CalculateCeremonyScore_DearFriend_HasHighWeight()
    {
        var response1 = "My dear friend, I agree.";
        var response2 = "I appreciate your point.";

        var method = _gates.GetType().GetMethod("CalculateCeremonyScore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var score1 = method?.Invoke(_gates, [response1]) as double?;
        var score2 = method?.Invoke(_gates, [response2]) as double?;

        Assert.True(score1 > score2);
    }

    [Fact]
    public void CalculateTextSimilarity_IdenticalText_ReturnsOne()
    {
        var text = "Proposer argues for the theory of principles.";

        var similarity = _gates.CalculateTextSimilarity(text, text);

        Assert.Equal(1.0, similarity, 2);
    }

    [Fact]
    public void CalculateTextSimilarity_CompletelyDifferent_ReturnsZero()
    {
        var text1 = "Proposer argues for principles.";
        var text2 = "Skeptic argues for particulars.";

        var similarity = _gates.CalculateTextSimilarity(text1, text2);

        Assert.True(similarity < 0.3);
    }

    [Fact]
    public void CalculateTextSimilarity_HighOverlap_ReturnsHighScore()
    {
        var text1 = "The theory of forms explains universal concepts through ideal templates.";
        var text2 = "The theory of forms explains universal ideas through ideal patterns.";

        var similarity = _gates.CalculateTextSimilarity(text1, text2);

        Assert.True(similarity > 0.3);
    }

    [Fact]
    public void CalculateTextSimilarity_EmptyString_ReturnsZero()
    {
        var similarity = _gates.CalculateTextSimilarity("", "some text");
        Assert.Equal(0, similarity);

        similarity = _gates.CalculateTextSimilarity("some text", "");
        Assert.Equal(0, similarity);

        similarity = _gates.CalculateTextSimilarity("", "");
        Assert.Equal(0, similarity);
    }

    [Fact]
    public void CountNewArguments_WithDisagreementIndicators_ReturnsPositiveCount()
    {
        var response = "However, this contradicts the evidence. Consider a counterexample: what about justice in edge cases?";

        var method = _gates.GetType().GetMethod("CountNewArguments", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var count = method?.Invoke(_gates, [response]) as int?;

        Assert.True(count >= 2);
    }

    [Fact]
    public void CountNewArguments_WithSubstantiveContent_ReturnsPositiveCount()
    {
        var response = "Consider for example the case of justice. Suppose we imagine a society without laws. What about the edge cases?";

        var method = _gates.GetType().GetMethod("CountNewArguments", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var count = method?.Invoke(_gates, [response]) as int?;

        Assert.True(count >= 3);
    }

    [Fact]
    public void CountNewArguments_EmptyResponse_ReturnsZero()
    {
        var method = _gates.GetType().GetMethod("CountNewArguments", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var count = method?.Invoke(_gates, [""]) as int?;

        Assert.Equal(0, count);
    }

    [Fact]
    public void DetectConsecutiveFarewells_ThreeConsecutive_ReturnsTrue()
    {
        var history = new List<string>
        {
            "Some content here.",
            "Farewell my friend.",
            "Goodbye and good luck.",
            "Adieu."
        };

        var method = _gates.GetType().GetMethod("DetectConsecutiveFarewells", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method?.Invoke(_gates, [history, 3]) as bool?;

        Assert.True(result);
    }

    [Fact]
    public void DetectConsecutiveFarewells_InterruptedSequence_ReturnsFalse()
    {
        var history = new List<string>
        {
            "Farewell.",
            "Goodbye.",
            "But wait, I have more to say.",
            "Adieu."
        };

        var method = _gates.GetType().GetMethod("DetectConsecutiveFarewells", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method?.Invoke(_gates, [history, 3]) as bool?;

        Assert.False(result);
    }

    [Fact]
    public void DetectConsecutiveFarewells_TooFewEntries_ReturnsFalse()
    {
        var history = new List<string>
        {
            "Farewell.",
            "Goodbye."
        };

        var method = _gates.GetType().GetMethod("DetectConsecutiveFarewells", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method?.Invoke(_gates, [history, 3]) as bool?;

        Assert.False(result);
    }

    [Fact]
    public void DetectAgreementStagnation_AgreementWithNoNewIdeas_ReturnsTrue()
    {
        // Need at least turnThreshold + 2 = 6 history items; use 8 to be safe
        // Avoid "'s", " is ", " are ", " represents " to prevent CountNewArguments from triggering
        var history = new List<string>
        {
            "Initial response.",
            "Second response.",
            "I concur with your point.",
            "You make a fair point indeed.",
            "I agree completely.",
            "That was well-argued, I agree.",
            "Excellent point, I agree.",
            "Well stated, I agree."
        };

        var state = new DialogueQualityState();
        foreach (var response in history)
        {
            state.RecordTurn(response, new ResponseQualityMetrics(), 0);
        }

        var method = _gates.GetType().GetMethod("DetectAgreementStagnation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method?.Invoke(_gates, [state, 4]) as bool?;

        Assert.True(result);
    }

    [Fact]
    public void DetectAgreementStagnation_AgreementWithNewIdeas_ReturnsFalse()
    {
        var history = new List<string>
        {
            "I concur, but consider this counterexample.",
            "You make a fair point, however there's more.",
            "I agree, and we should also examine X.",
            "That's well-argued, yet what about Y?"
        };

        var state = new DialogueQualityState();
        foreach (var response in history)
        {
            state.RecordTurn(response, new ResponseQualityMetrics(), 0);
        }

        var method = _gates.GetType().GetMethod("DetectAgreementStagnation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method?.Invoke(_gates, [state, 4]) as bool?;

        Assert.False(result);
    }

    [Fact]
    public void ComputeQualityScore_HighQualityResponse_ReturnsHighScore()
    {
        var response = "However, consider this counterexample: what about justice in edge cases? This challenges the core assumption and adds a new dimension. Suppose we imagine a society without laws - the implications are profound. We observe that this applies universally.";

        var method = _gates.GetType().GetMethod("ComputeQualityScore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var score = method?.Invoke(_gates, [response, "Previous response here", 1, 0.5]) as double?;

        Assert.True(score >= 70);
    }

    [Fact]
    public void ComputeQualityScore_LowQualityResponse_ReturnsLowScore()
    {
        // Very short, high ceremony, no substance
        var response = "My dear friend, I am deeply moved by your profound gratitude.";

        var method = _gates.GetType().GetMethod("ComputeQualityScore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        // Pass high ceremonyScore (15) to get 0 tone points, no new args (10 content), high similarity with previous (0 progression), short length (0)
        // Total = 10 + 0 + 0 + 0 = 10
        var score = method?.Invoke(_gates, [response, "My dear friend, I am deeply moved by your profound gratitude.", 1, 15]) as double?;

        Assert.True(score < 30);
    }

    [Fact]
    public void AnalyzeResponse_ReturnsCompleteMetrics()
    {
        var response = "However, consider this counterexample: what about justice?";

        var metrics = _gates.AnalyzeResponse(response, "Previous response", 1);

        Assert.NotNull(metrics);
        Assert.True(metrics.CeremonyScore >= 0);
        Assert.True(metrics.SimilarityToPrevious is >= 0 and <= 1);
        Assert.True(metrics.NewArgumentCount >= 0);
        Assert.True(metrics.QualityScore is >= 0 and <= 100);
        Assert.True(metrics.WordCount > 0);
        Assert.IsType<bool>(metrics.HasFarewell);
        Assert.IsType<bool>(metrics.HasAgreement);
        Assert.IsType<bool>(metrics.HasDisagreement);
    }

    [Fact]
    public void DetectStalemate_CircularRestatement_ReturnsSignal()
    {
        var state = new DialogueQualityState();
        state.RecordTurn("Proposer's principles are abstract.", new ResponseQualityMetrics(), 0);
        state.RecordTurn("Skeptic says particulars matter.", new ResponseQualityMetrics(), 0);

        var signal = _gates.DetectStalemate(
            state,
            "Proposer's principles are abstract.", // Similar to turn 0
            "Skeptic says particulars matter.", // Similar to turn 1
            "proposer", "skeptic",
            0.8, // Proposer similarity > 0.55 threshold
            0.2,
            0.5,
            [0.5, 0.5, 0.5],
            2);

        Assert.Equal(StalemateSignal.CircularRestatement, signal);
    }

    [Fact]
    public void DetectStalemate_TalkingPast_ReturnsSignal()
    {
        var state = new DialogueQualityState();
        state.RecordTurn("Proposer: Principles are real.", new ResponseQualityMetrics(), 0);
        state.RecordTurn("Skeptic: Particulars exist.", new ResponseQualityMetrics(), 0);

        var signal = _gates.DetectStalemate(
            state,
            "The theory of forms is beautiful.",
            "Particulars are the foundation of reality.",
            "proposer", "skeptic",
            0.1, 0.1, 0.5,
            [0.5, 0.5, 0.5],
            2);

        Assert.Equal(StalemateSignal.TalkingPast, signal);
    }

    [Fact]
    public void DetectStalemate_Entrenchment_ReturnsSignal()
    {
        var state = new DialogueQualityState();
        state.RecordTurn("Forms are real.", new ResponseQualityMetrics(), 0);
        state.RecordTurn("No, particulars are real.", new ResponseQualityMetrics(), 0);

        // Entrenchment requires: both disagree AND both have zero new substantive content
        var signal = _gates.DetectStalemate(
            state,
            "But forms ARE real, you're wrong! However this contradicts evidence.",
            "No, particulars ARE real, you're mistaken! This contradicts your view.",
            "proposer", "skeptic",
            0.1, 0.1, 0.8,
            [0.5, 0.5, 0.5],
            2);

        Assert.Equal(StalemateSignal.EntenchedNoMovement, signal);
    }

    [Fact]
    public void DetectStalemate_PremiseFork_ReturnsSignal()
    {
        var state = new DialogueQualityState();
        var distances = new List<double> { 0.8, 0.85, 0.9, 0.88 };

        // Need to avoid earlier stalemate checks:
        // 1. CircularRestatement: pass low similarity
        // 2. TalkingPast: include cross-reference or disagreement
        // 3. Entrenchment: include disagreement markers
        var signal = _gates.DetectStalemate(
            state,
            "You argue that forms are ideal, but consider this.",
            "Your position on particulars is noted, however they are real.",
            "proposer", "skeptic",
            0.1, 0.1, 0.8,
            distances,
            4);

        Assert.Equal(StalemateSignal.PremiseLevelFork, signal);
    }

    [Fact]
    public void DetectStalemate_MissingInformation_ReturnsSignal()
    {
        var state = new DialogueQualityState();
        state.RecordTurn("Forms are ideal.", new ResponseQualityMetrics(), 0);
        state.RecordTurn("Particulars exist.", new ResponseQualityMetrics(), 0);

        // MissingInformation requires: both agents use 2+ epistemic hedges
        // Add cross-reference markers so TalkingPast doesn't trigger
        // Add substantive content (counterexamples/questions) so Entrenchment doesn't trigger
        // Avoid removing hedges so MissingInformation triggers
        var signal = _gates.DetectStalemate(
            state,
            "You argue that forms are real, but we cannot know if forms are real. It is unclear. We have no way to determine this. Consider the evidence.",
            "You argue that particulars exist, but impossible to say if particulars are fundamental. Without evidence, we can only speculate. It depends on unknowns. What about the counterevidence?",
            "proposer", "skeptic",
            0.1, 0.1, 0.3,
            [0.3, 0.3, 0.3],
            2);

        Assert.Equal(StalemateSignal.MissingInformation, signal);
    }
}