using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using SeldonLattice.Application.Agents.Dialecticians;
using SeldonLattice.Application.Services;
using SeldonLattice.Domain.Models.Cycle;
using SeldonLattice.Domain.Options;
using Xunit;

namespace SeldonLattice.Application.Tests;

/// <summary>
/// Phase 0 of the Reasoning Integrity Layer: [ABSTAIN] gate-loop and root-thesis
/// anchoring in debate prompts.
/// </summary>
public class ReasoningIntegrityPhase0Tests
{
    /// <summary>Theme embeds to (1,0); any other text to (0,1) -> cosine 0 (maximally off-topic).</summary>
    private sealed class OrthogonalEmbeddingService : IEmbeddingGenerator<string, Embedding<float>>
    {
        private static float[] Vec(string v) => v == "the root question" ? [1f, 0f] : [0f, 1f];

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken ct = default)
        {
            var results = values.Select(v => new Embedding<float>(Vec(v))).ToList();
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(results));
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceKey is not null) return null;
            if (serviceType == typeof(EmbeddingGeneratorMetadata))
                return new EmbeddingGeneratorMetadata(nameof(OrthogonalEmbeddingService));
            return serviceType.IsInstanceOfType(this) ? this : null;
        }

        public void Dispose() { }
    }

    private static DialogueQualityGates CreateGates() => new(
        new QualityGateOptions
        {
            Enabled = true,
            MaxTurns = 16,
            ConsecutiveFarewellsThreshold = 3,
            SimilarityThreshold = 0.85,
            AgreementStagnationTurns = 4,
            LowQualityConsecutiveTurns = 99,
            RelevanceGateEnabled = true,
            OnThemeRelevanceThreshold = 0.28,
            MinTurnsForRelevanceCheck = 1,
            RelevanceConsecutiveTurns = 2,
        },
        NullLogger<DialogueQualityGates>.Instance,
        securityScanner: null,
        embeddingService: new OrthogonalEmbeddingService());

    [Theory]
    [InlineData("[ABSTAIN]")]
    [InlineData("[abstain]")]
    [InlineData("[ABSTAIN]: cannot ground this without the dataset")]
    [InlineData("[ABSTAIN] no reliable source links this to the root")]
    [InlineData("  [ABSTAIN]  ")]
    public void IsAbstention_RecognizesBareAndShortReasonForms(string text)
    {
        Assert.True(DialogueQualityGates.IsAbstention(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("I will [ABSTAIN] but here is a full argument that keeps going and going until it is clearly not an abstention at all, quite the opposite really.")]
    [InlineData("[ABSTAIN]x this token is immediately followed by prose")]
    [InlineData("Normal philosophical response that disagrees with the previous claim.")]
    public void IsAbstention_RejectsProseAndMalformedForms(string text)
    {
        Assert.False(DialogueQualityGates.IsAbstention(text));
    }

    [Fact]
    public async Task OffTopicControl_AccumulatesAndForcesExit()
    {
        var gates = CreateGates();
        var state = new DialogueQualityState();

        Assert.Null(await gates.EvaluateAndLogAsync(state, "an unrelated digression", null, 1, "the root question"));
        var exit = await gates.EvaluateAndLogAsync(state, "another unrelated digression", null, 2, "the root question");

        Assert.Equal(DialogueExitReason.OffTopicDrift, exit);
    }

    [Fact]
    public async Task Abstention_IsNoPenaltyAndResetsDriftLadder()
    {
        var gates = CreateGates();
        var state = new DialogueQualityState { ConsecutiveOffTopicTurns = 1 };

        var exit = await gates.EvaluateAndLogAsync(state, "[ABSTAIN]", null, 2, "the root question");

        Assert.Null(exit);
        Assert.Equal(0, state.ConsecutiveOffTopicTurns);
    }

    [Fact]
    public async Task Abstention_DoesNotTripRepetitionOrSimilarityGates()
    {
        var gates = CreateGates();
        var state = new DialogueQualityState();
        state.ResponseHistory.Add("[ABSTAIN]");

        // Same token twice would be maximum similarity; abstention must still pass.
        var exit = await gates.EvaluateAndLogAsync(state, "[ABSTAIN]", "[ABSTAIN]", 3, "the root question");

        Assert.Null(exit);
    }

    [Fact]
    public async Task MaxTurns_StillTerminatesEvenOnAbstention()
    {
        var gates = CreateGates();
        var state = new DialogueQualityState();

        var exit = await gates.EvaluateAndLogAsync(state, "[ABSTAIN]", null, 16, "the root question");

        Assert.Equal(DialogueExitReason.MaxTurnsExceeded, exit);
    }

    [Fact]
    public void RootAnchoring_IncludesRootAndLinkBackClause()
    {
        var method = typeof(DialecticianBase).GetMethod(
            "BuildRootThemeAnchoring",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var text = method!.Invoke(null, ["Is free will an illusion?", "Stay physical."]) as string;

        Assert.NotNull(text);
        Assert.Contains("Is free will an illusion?", text);
        Assert.Contains("cannot be linked back to the root", text);
    }

    [Fact]
    public void RootAnchoring_EmptyWhenNoRoot()
    {
        var method = typeof(DialecticianBase).GetMethod(
            "BuildRootThemeAnchoring",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.Equal(string.Empty, method!.Invoke(null, [null, "fallback"]) as string);
    }
}