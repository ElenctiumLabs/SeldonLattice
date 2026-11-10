using Microsoft.Extensions.Logging;
using SeldonLattice.Application.Services;
using SeldonLattice.Domain.Models;
using SeldonLattice.Domain.Models.Cycle;
using SeldonLattice.Domain.Options;
using Xunit;

namespace SeldonLattice.Application.Tests;

public class QualityGateServiceTests
{
    private readonly DialogueQualityGates _gates;
    private readonly QualityGateOptions _options;

    public QualityGateServiceTests()
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
            PremiseForkSustainedTurns = 3,
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<DialogueQualityGates>();

        _gates = new DialogueQualityGates(_options, logger);
    }

    [Fact]
    public void AnalyzeResponse_ReturnsMetricsWithValidRanges()
    {
        var response = "However, consider this counterexample: what about justice?";
        var metrics = _gates.AnalyzeResponse(response, "Previous response", 1);

        Assert.NotNull(metrics);
        Assert.InRange(metrics.CeremonyScore, 0, double.MaxValue);
        Assert.InRange(metrics.SimilarityToPrevious, 0, 1);
        Assert.InRange(metrics.NewArgumentCount, 0, int.MaxValue);
        Assert.InRange(metrics.QualityScore, 0, 100);
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
            "Proposer's principles are abstract.",
            "Skeptic says particulars matter.",
            "proposer", "skeptic",
            0.8, 0.2, 0.5,
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

    [Fact]
    public void DetectStalemate_NoStalemate_ReturnsNone()
    {
        var state = new DialogueQualityState();
        state.RecordTurn("Proposer's principles are abstract.", new ResponseQualityMetrics(), 0);
        state.RecordTurn("Skeptic says particulars matter.", new ResponseQualityMetrics(), 0);

        var signal = _gates.DetectStalemate(
            state,
            "However, consider this counterexample about justice.",
            "This challenges the core assumption and adds a new dimension.",
            "proposer", "skeptic",
            0.1, 0.1, 0.2,
            [0.3, 0.3, 0.3],
            2);

        Assert.Null(signal);
    }

    [Fact]
    public void ComputeQualityScore_HighQuality_ReturnsHighScore()
    {
        var response = "However, consider this counterexample: what about justice? This challenges the core assumption and adds a new dimension. Suppose we imagine a society without laws. The implications are profound. We observe that this applies universally.";

        var method = typeof(DialogueQualityGates).GetMethod("ComputeQualityScore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var score = method?.Invoke(_gates, [response, "Previous response here", 1, 0.5]) as double?;

        Assert.True(score >= 70);
    }

    [Fact]
    public void ComputeQualityScore_LowQuality_ReturnsLowScore()
    {
        var response = "My dear friend, I am deeply moved by your profound gratitude.";

        var method = typeof(DialogueQualityGates).GetMethod("ComputeQualityScore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var score = method?.Invoke(_gates, [response, "My dear friend, I am deeply moved by your profound gratitude.", 1, 15]) as double?;

        Assert.True(score < 30);
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

        var method = typeof(DialogueQualityGates).GetMethod("DetectConsecutiveFarewells", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
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

        var method = typeof(DialogueQualityGates).GetMethod("DetectConsecutiveFarewells", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method?.Invoke(_gates, [history, 3]) as bool?;

        Assert.False(result);
    }

    [Fact]
    public void CalculateTextSimilarity_Identical_ReturnsOne()
    {
        var text = "Proposer argues for the theory of principles.";
        var similarity = _gates.CalculateTextSimilarity(text, text);
        Assert.Equal(1.0, similarity, 2);
    }

    [Fact]
    public void CalculateTextSimilarity_CompletelyDifferent_ReturnsNearZero()
    {
        var text1 = "Proposer argues for principles.";
        var text2 = "Skeptic argues for particulars.";
        var similarity = _gates.CalculateTextSimilarity(text1, text2);
        Assert.True(similarity < 0.3);
    }

    [Fact]
    public void CalculateTextSimilarity_EmptyString_ReturnsZero()
    {
        Assert.Equal(0, _gates.CalculateTextSimilarity("", "some text"));
        Assert.Equal(0, _gates.CalculateTextSimilarity("some text", ""));
        Assert.Equal(0, _gates.CalculateTextSimilarity("", ""));
    }
}