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
/// Regression tests for next-theme parsing and sanitisation.
///
/// The motivating failure (2026-11-09): <see cref="ModeratorBase"/> sanitised a theme by
/// splitting it on '.', '!' and '?' and keeping the first element, which discarded the
/// delimiter. Every ascended rung in the "What is a question that should not be asked?"
/// debate was stored without its question mark, so the question accelerator returned
/// rungs that read as statements rather than questions.
///
/// These tests pin: the terminating punctuation survives; the first sentence still wins
/// over the rest of the reply; and a non-positive length cap still means "no limit"
/// (the cf58234 regression, which was previously untested).
/// </summary>
public class NextThemeParsingTests
{
    /// <summary>Minimal moderator that exposes the protected next-theme parser/sanitiser.</summary>
    private sealed class TestModerator(Kernel kernel, DialecticsOptions options)
        : ModeratorBase(kernel, Options.Create(options), new NoOpProviderRateLimiter())
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

        /// <summary>Test hook into the protected next-theme parser/sanitiser.</summary>
        public NextThemeResult ParseNextTheme(string raw) => ParseNextThemeResult(raw);
    }

    private static TestModerator Build(DialecticsOptions? options = null)
        => new(Kernel.CreateBuilder().Build(), options ?? new DialecticsOptions());

    [Fact]
    public void QuestionLine_KeepsItsQuestionMark()
    {
        const string raw = """
            QUESTION: What is a question that should not be asked?
            GENERALIZES: It generalizes the root.
            """;

        var result = Build().ParseNextTheme(raw);

        Assert.Equal("What is a question that should not be asked?", result.Theme);
        Assert.Equal("It generalizes the root.", result.ConnectiveSentence);
    }

    [Fact]
    public void ProductionShape_KeepsItsQuestionMark()
    {
        // The rung stored at level 1 of the 2026-11-09 debate, before the fix.
        const string question = "What kind of inquiry becomes impossible not due to lack of knowledge, but because its very asking alters the reality it seeks";
        var raw = "QUESTION: " + question + "?\nGENERALIZES: The inquiry turns on the act of asking itself.";

        var result = Build().ParseNextTheme(raw);

        Assert.Equal(question + "?", result.Theme);
    }

    [Fact]
    public void MultiSentenceTheme_StopsAtFirstSentence()
    {
        const string raw = """
            QUESTION: What is X? It is Y.
            GENERALIZES: Z
            """;

        var result = Build().ParseNextTheme(raw);

        Assert.Equal("What is X?", result.Theme);
    }

    [Fact]
    public void PeriodTerminator_IsKept()
    {
        const string raw = """
            QUESTION: What is X.
            GENERALIZES: Y
            """;

        var result = Build().ParseNextTheme(raw);

        Assert.Equal("What is X.", result.Theme);
    }

    [Fact]
    public void NoTerminator_TextIsUnchanged()
    {
        const string raw = """
            QUESTION: What is X
            GENERALIZES: Y
            """;

        var result = Build().ParseNextTheme(raw);

        Assert.Equal("What is X", result.Theme);
    }

    [Fact]
    public void QuestionLineWithoutGeneralizes_YieldsEmptyConnective()
    {
        const string raw = """
            QUESTION: What is X?
            """;

        var result = Build().ParseNextTheme(raw);

        Assert.Equal("What is X?", result.Theme);
        Assert.Equal(string.Empty, result.ConnectiveSentence);
    }

    [Fact]
    public void UnlabelledTheme_UsesTextBeforeGeneralizes_AndConnectiveTakesFirstLine()
    {
        const string raw = """
            What is X?
            GENERALIZES: "This generalizes the root.
            """;

        var result = Build().ParseNextTheme(raw);

        Assert.Equal("What is X?", result.Theme);
        Assert.Equal("This generalizes the root.", result.ConnectiveSentence);
    }

    [Fact]
    public void ZeroThemeCap_DoesNotTruncate()
    {
        // cf58234: a non-positive cap means "no limit". Treating 0 as a real length made
        // every theme the empty string and silently removed the next-theme path.
        const string question = "What is a question that should not be asked, and what would have to be true of a question for its asking to be worse than the silence it breaks even when the answer is already known?";
        Assert.True(question.Length > 180, "test question should exceed the default cap");

        var raw = "QUESTION: " + question + "\nGENERALIZES: Y";
        var options = new DialecticsOptions { ModeratorThemeMaxLength = 0 };

        var result = Build(options).ParseNextTheme(raw);

        Assert.Equal(question, result.Theme);
    }

    [Fact]
    public void PositiveThemeCap_Truncates()
    {
        const string raw = """
            QUESTION: What is a question that should not be asked?
            GENERALIZES: Y
            """;
        var options = new DialecticsOptions { ModeratorThemeMaxLength = 20 };

        var result = Build(options).ParseNextTheme(raw);

        Assert.Equal("What is a question t", result.Theme);
    }

    [Fact]
    public void EmptyInput_ReturnsEmptyThemeAndConnective()
    {
        var result = Build().ParseNextTheme("");

        Assert.Equal(string.Empty, result.Theme);
        Assert.Equal(string.Empty, result.ConnectiveSentence);
    }

    [Fact]
    public void ExhaustedReply_MarksExhausted_AndKeepsTheReason()
    {
        const string raw = "EXHAUSTED: every further question would only restate a rung already climbed.";

        var result = Build().ParseNextTheme(raw);

        Assert.True(result.IsExhausted);
        Assert.Equal(string.Empty, result.Theme);
        Assert.Equal("every further question would only restate a rung already climbed.", result.ExhaustionReason);
    }

    [Fact]
    public void ExhaustedWithoutReason_UsesPlaceholder()
    {
        var result = Build().ParseNextTheme("EXHAUSTED");

        Assert.True(result.IsExhausted);
        Assert.Equal("The moderator gave no reason.", result.ExhaustionReason);
    }

    [Fact]
    public void ExhaustedWordInsideAQuestion_IsNotMistakenForDeclining()
    {
        // The marker must be a standalone word: a rung that begins "Exhausted by..." is a
        // question, not a decline.
        const string raw = "QUESTION: What is exhausted by the inquiry?\nGENERALIZES: It generalizes the root.";

        var result = Build().ParseNextTheme(raw);

        Assert.False(result.IsExhausted);
        Assert.Equal("What is exhausted by the inquiry?", result.Theme);
        Assert.Null(result.ExhaustionReason);
    }

    [Fact]
    public void NormalReply_IsNotExhausted()
    {
        const string raw = "QUESTION: What is X?\nGENERALIZES: Y";

        var result = Build().ParseNextTheme(raw);

        Assert.False(result.IsExhausted);
        Assert.Null(result.ExhaustionReason);
    }
}
