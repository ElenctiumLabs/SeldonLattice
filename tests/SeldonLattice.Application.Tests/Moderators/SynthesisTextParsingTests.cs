using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Application.Agents.Moderators;
using SeldonLattice.Domain.Models.Agents;
using SeldonLattice.Domain.Models.Cycle;
using SeldonLattice.Domain.Models.Results;
using SeldonLattice.Domain.Options;
using Xunit;

namespace SeldonLattice.Application.Tests.Moderators;

/// <summary>
/// Regression tests for the DOMAIN/THESIS/METAPHOR plain-text synthesis schema.
///
/// The motivating failure (2026-11-09): the "What makes a question worth asking?" run stored a
/// whole labelled reply as the metaphor at level 3, because the model omitted the METAPHOR label
/// and left the prose unlabelled. The parser's all-or-nothing guard rejected the reply and its
/// fallback returned the raw text as the metaphor.
///
/// These tests pin: the unlabelled prose is recovered; the labelled shape is unchanged; and a
/// reply missing DOMAIN or THESIS still falls back.
/// </summary>
public class SynthesisTextParsingTests
{
    /// <summary>Minimal moderator that exposes the protected text parser.</summary>
    private sealed class TestModerator(Kernel kernel)
        : ModeratorBase(kernel, Options.Create(new DialecticsOptions()), new NoOpProviderRateLimiter())
    {
        protected override string FallbackImageDomain => "fallback-domain";

        protected override MetaphorResult FallbackResult => new(
            "FALLBACK METAPHOR", FallbackImageDomain, "FALLBACK THESIS",
            "FALLBACK NEXT", "FALLBACK CONNECTIVE");

        public override ChatHistory CreateSynthesisHistory(
            string theme, AgentResponse[] previousResponses,
            string moderationFocus, string cyclePhase = "growth")
            => [];

        protected override string BuildInterjectionPrompt(
            StalemateSignal signal, string theme, string d1Snippet, string d2Snippet,
            string d1Name, string d2Name) => string.Empty;

        /// <summary>Test hook into the protected text parser.</summary>
        public MetaphorResult ParseText(string raw) => ParseTextSynthesisResponse(raw);
    }

    private static TestModerator Build() => new(Kernel.CreateBuilder().Build());

    [Fact]
    public void MissingMetaphorLabel_RecoversTheUnlabelledProse()
    {
        // Verbatim shape from level 3 of the 2026-11-09 run: four labels present, METAPHOR
        // absent, the metaphor prose following REPEATS PREMISE with no label.
        const string raw = """
            DOMAIN: weaving
            THESIS: An answer is worth receiving not when one returns repeatedly out of necessity, but when one weaves presence into absence through a practice that honors stillness as much as motion.
            REMAINING DISSENSUS: Whether silence can be truly heard without the risk of turning listening into an act of demand.
            REPEATS PREMISE: 0.2
            The weaver does not measure worth by how often she returns to the loom, nor by how tightly her thread grips the frame.
            """;

        var result = Build().ParseText(raw);

        Assert.Equal("The weaver does not measure worth by how often she returns to the loom, nor by how tightly her thread grips the frame.", result.Metaphor);
        Assert.Equal("weaving", result.ImageDomain);
        Assert.Equal("An answer is worth receiving not when one returns repeatedly out of necessity, but when one weaves presence into absence through a practice that honors stillness as much as motion.", result.Thesis);
        Assert.Equal("Whether silence can be truly heard without the risk of turning listening into an act of demand.", result.RemainingDissensus);
        Assert.Equal(0.2f, result.RepeatsPremise);
    }

    [Fact]
    public void LabelledResponse_ParsesEveryField()
    {
        const string raw = """
            DOMAIN: clockwork
            THESIS: A mechanism keeps time without intending to.
            REMAINING DISSENSUS: None
            REPEATS PREMISE: 0.5
            METAPHOR: A pocket watch wound every dawn keeps time because someone remembers to wind it.
            """;

        var result = Build().ParseText(raw);

        Assert.Equal("A pocket watch wound every dawn keeps time because someone remembers to wind it.", result.Metaphor);
        Assert.Equal("clockwork", result.ImageDomain);
        Assert.Equal("A mechanism keeps time without intending to.", result.Thesis);
        Assert.Null(result.RemainingDissensus);   // "None" means resolved, not unanswered.
        Assert.Equal(0.5f, result.RepeatsPremise);
    }

    [Fact]
    public void MissingMetaphorLabel_WithNoUnlabelledProse_FallsBack()
    {
        // Every line is labelled and there is no prose to recover: the reply is unusable, so
        // the caller must see an incomplete parse and retry rather than store the labels.
        const string raw = """
            DOMAIN: weaving
            THESIS: An answer is worth receiving.
            REPEATS PREMISE: 0.2
            """;

        var result = Build().ParseText(raw);

        Assert.Equal(string.Empty, result.ImageDomain);
        Assert.Equal(string.Empty, result.Thesis);
    }

    [Fact]
    public void MissingDomain_FallsBack()
    {
        const string raw = """
            THESIS: An answer is worth receiving.
            METAPHOR: The weaver does not measure worth by how often she returns to the loom.
            """;

        var result = Build().ParseText(raw);

        Assert.Equal(string.Empty, result.ImageDomain);
        Assert.Equal(string.Empty, result.Thesis);
    }

    [Fact]
    public void MissingThesis_FallsBack()
    {
        const string raw = """
            DOMAIN: weaving
            METAPHOR: The weaver does not measure worth by how often she returns to the loom.
            """;

        var result = Build().ParseText(raw);

        Assert.Equal(string.Empty, result.ImageDomain);
        Assert.Equal(string.Empty, result.Thesis);
    }

    [Fact]
    public void DomainAfterThesis_FallsBack()
    {
        // Out of order: DOMAIN must precede THESIS, otherwise the slices would run backwards.
        const string raw = """
            THESIS: An answer is worth receiving.
            DOMAIN: weaving
            METAPHOR: The weaver does not measure worth by how often she returns to the loom.
            """;

        var result = Build().ParseText(raw);

        Assert.Equal(string.Empty, result.ImageDomain);
        Assert.Equal(string.Empty, result.Thesis);
    }

    [Fact]
    public void MetaphorBeforeThesis_FallsBack()
    {
        const string raw = """
            DOMAIN: weaving
            METAPHOR: The weaver does not measure worth by how often she returns to the loom.
            THESIS: An answer is worth receiving.
            """;

        var result = Build().ParseText(raw);

        Assert.Equal(string.Empty, result.ImageDomain);
        Assert.Equal(string.Empty, result.Thesis);
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        var result = Build().ParseText("");

        Assert.Equal(string.Empty, result.Metaphor);
        Assert.Equal(string.Empty, result.ImageDomain);
        Assert.Equal(string.Empty, result.Thesis);
    }
}
