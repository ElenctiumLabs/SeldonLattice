using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Application.Agents.Moderators;
using SeldonLattice.Domain.Options;
using Xunit;

namespace SeldonLattice.Application.Tests.Moderators;

/// <summary>
/// Ladder memory in the next-theme prompt.
///
/// The motivating failure (2026-11-09): the moderator was handed the root, the current question and
/// the synthesis metaphor, and nothing else. It could not see the rungs it had already climbed, so
/// consecutive rungs restated one insight in new words - a semantic restatement no lexical metric
/// catches. It also never saw the synthesis thesis, only the metaphor story, so the primary path
/// lost the one thing the fallback path had.
///
/// These tests pin that the rendered prompt now carries the ordered rungs and the thesis, that an
/// empty ladder and a missing thesis render as explicit placeholders rather than a blank section,
/// and that the recovery prompt carries the same memory.
/// </summary>
public class NextThemePromptTests
{
    private static (MediatorAgent Agent, CapturingChatCompletionService Service) BuildAgent()
    {
        var service = new CapturingChatCompletionService();
        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton<IChatCompletionService>(service);
        var kernel = builder.Build();

        var agent = new MediatorAgent(
            kernel,
            Options.Create(new DialecticsOptions()),
            new NoOpProviderRateLimiter(),
            dialectician1Name: "Proposer",
            dialectician2Name: "Skeptic",
            dialogueLabel: "philosophical");

        return (agent, service);
    }

    private const string QuestionReply =
        "QUESTION: What is the limit of explanation?\nGENERALIZES: It extends the root.";

    [Fact]
    public async Task RenderedPrompt_ContainsClimbedRungsAndThesis()
    {
        var (agent, service) = BuildAgent();
        service.Responses["Propose the next concrete question"] = QuestionReply;

        await agent.SynthesizeNextThemeAsync(
            metaphor: "The ladder is a proof by induction.",
            currentTheme: "What is the ground of explanation?",
            rootTheme: "What is the ground of anything?",
            climbedThemes: ["What is the ground of explanation?", "What makes a reason sufficient?"],
            synthesisThesis: "Every explanation rests on an unexamined primitive.");

        var prompt = Assert.Single(service.PromptsSeen);

        Assert.Contains("Rung 1: What is the ground of explanation?", prompt);
        Assert.Contains("Rung 2: What makes a reason sufficient?", prompt);
        Assert.Contains("Every explanation rests on an unexamined primitive.", prompt);
    }

    [Fact]
    public async Task RenderedPrompt_WithoutRungs_ShowsPlaceholder()
    {
        var (agent, service) = BuildAgent();
        service.Responses["Propose the next concrete question"] = QuestionReply;

        await agent.SynthesizeNextThemeAsync("a metaphor", "current", "root");

        var prompt = Assert.Single(service.PromptsSeen);

        Assert.Contains("(none yet)", prompt);
    }

    [Fact]
    public async Task RenderedPrompt_WithoutThesis_ShowsPlaceholder()
    {
        var (agent, service) = BuildAgent();
        service.Responses["Propose the next concrete question"] = QuestionReply;

        await agent.SynthesizeNextThemeAsync("a metaphor", "current", "root",
            climbedThemes: ["an earlier rung"]);

        var prompt = Assert.Single(service.PromptsSeen);

        Assert.Contains("(no thesis was recorded)", prompt);
    }

    [Fact]
    public async Task RenderedPrompt_RungsAppearInOrder()
    {
        var (agent, service) = BuildAgent();
        service.Responses["Propose the next concrete question"] = QuestionReply;

        await agent.SynthesizeNextThemeAsync("a metaphor", "current", "root",
            climbedThemes: ["the first rung", "the second rung", "the third rung"]);

        var prompt = Assert.Single(service.PromptsSeen);

        var first = prompt.IndexOf("Rung 1:", StringComparison.Ordinal);
        var second = prompt.IndexOf("Rung 2:", StringComparison.Ordinal);
        var third = prompt.IndexOf("Rung 3:", StringComparison.Ordinal);

        Assert.True(first >= 0 && second > first && third > second,
            "rungs must be rendered oldest first");
    }

    [Fact]
    public async Task RenderedPrompt_StatesTheAccountabilityDirective()
    {
        var (agent, service) = BuildAgent();
        service.Responses["Propose the next concrete question"] = QuestionReply;

        await agent.SynthesizeNextThemeAsync("a metaphor", "current", "root",
            climbedThemes: ["an earlier rung"]);

        var prompt = Assert.Single(service.PromptsSeen);

        Assert.Contains("not already covered by the rungs", prompt);
        Assert.Contains("Do not repeat the current question or any earlier rung.", prompt);
    }

    [Fact]
    public async Task RecoveryPrompt_AlsoCarriesTheLadder()
    {
        var (agent, service) = BuildAgent();
        // A reply with no QUESTION line and nothing before GENERALIZES parses to an empty theme,
        // which is what triggers the recovery attempt.
        service.Responses["Propose the next concrete question"] = "GENERALIZES: nothing else";
        service.Responses["Write one concrete next question"] = QuestionReply;

        await agent.SynthesizeNextThemeAsync("a metaphor", "current", "root",
            climbedThemes: ["the first rung", "the second rung"]);

        Assert.Equal(2, service.PromptsSeen.Count);

        var recovery = service.PromptsSeen[1];
        Assert.Contains("Rung 1: the first rung", recovery);
        Assert.Contains("Rung 2: the second rung", recovery);
    }
}
