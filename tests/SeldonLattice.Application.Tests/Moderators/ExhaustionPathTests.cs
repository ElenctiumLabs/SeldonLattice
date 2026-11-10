using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Application.Agents.Moderators;
using SeldonLattice.Domain.Options;
using Xunit;

namespace SeldonLattice.Application.Tests.Moderators;

/// <summary>
/// The gate that answers for an exhaustion claim.
///
/// The moderator can now decline to propose, which means an inquiry can end without the gate ever
/// rejecting a candidate. Without a check, a moderator that finds declining cheaper than proposing
/// would end every inquiry at the first hard rung. The gate judges the claim itself - whether the
/// inquiry has genuinely reached its limit - with the ladder in view.
///
/// These tests pin the exhaustion prompt and the moderator's new decline option.
/// </summary>
public class ExhaustionPathTests
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

    private const string ExhaustedReply = "EXHAUSTED: every further question would only restate a rung already climbed.";
    private const string ConfirmedVerdict = """{"valid": true, "reasoning": "The ladder has nowhere left to go."}""";

    [Fact]
    public async Task ThemePrompt_OffersTheExhaustedOption()
    {
        var (agent, service) = BuildAgent();
        service.Responses["Propose the next concrete question"] = ExhaustedReply;

        await agent.SynthesizeNextThemeAsync("a metaphor", "current", "root",
            climbedThemes: ["an earlier rung"]);

        var prompt = Assert.Single(service.PromptsSeen);

        Assert.Contains("EXHAUSTED:", prompt);
        Assert.Contains("do not invent one", prompt);
    }

    [Fact]
    public async Task ExhaustedReply_IsParsedAsADecline()
    {
        var (agent, service) = BuildAgent();
        service.Responses["Propose the next concrete question"] = ExhaustedReply;

        var result = await agent.SynthesizeNextThemeAsync("a metaphor", "current", "root",
            climbedThemes: ["an earlier rung"]);

        Assert.True(result.IsExhausted);
        Assert.Equal(string.Empty, result.Theme);
        Assert.Equal("every further question would only restate a rung already climbed.", result.ExhaustionReason);
    }

    [Fact]
    public async Task ExhaustionGatePrompt_ContainsRungsAndReason()
    {
        var (agent, service) = BuildAgent();
        service.Responses["declined to propose the next rung"] = ConfirmedVerdict;

        var verdict = await agent.EvaluateExhaustionAsync(
            rootTheme: "What is the ground of anything?",
            exhaustionReason: "Every further question would restate a rung already climbed.",
            climbedThemes: ["What is the ground of explanation?", "What makes a claim true?"]);

        var prompt = Assert.Single(service.PromptsSeen);

        Assert.Contains("Every further question would restate a rung already climbed.", prompt);
        Assert.Contains("Rung 1: What is the ground of explanation?", prompt);
        Assert.Contains("Rung 2: What makes a claim true?", prompt);
        Assert.True(verdict.IsValidGeneralization);
    }

    [Fact]
    public async Task ExhaustionGatePrompt_StatesTheStandard()
    {
        var (agent, service) = BuildAgent();
        service.Responses["declined to propose the next rung"] = ConfirmedVerdict;

        await agent.EvaluateExhaustionAsync("root", "the rung is spent");

        var prompt = Assert.Single(service.PromptsSeen);

        Assert.Contains("harder than saying there is none", prompt);
        Assert.Contains("genuinely reached its limit", prompt);
    }

    [Fact]
    public async Task ExhaustionGatePrompt_WithoutRungs_ShowsPlaceholder()
    {
        var (agent, service) = BuildAgent();
        service.Responses["declined to propose the next rung"] = ConfirmedVerdict;

        await agent.EvaluateExhaustionAsync("root", "the rung is spent");

        var prompt = Assert.Single(service.PromptsSeen);

        Assert.Contains("(none yet)", prompt);
    }

    [Fact]
    public async Task RecoveryPrompt_AlsoOffersTheExhaustedOption()
    {
        var (agent, service) = BuildAgent();
        service.Responses["Propose the next concrete question"] = "GENERALIZES: nothing else";
        service.Responses["Write one concrete next question"] = ExhaustedReply;

        await agent.SynthesizeNextThemeAsync("a metaphor", "current", "root",
            climbedThemes: ["an earlier rung"]);

        Assert.Equal(2, service.PromptsSeen.Count);

        var recovery = service.PromptsSeen[1];
        Assert.Contains("EXHAUSTED:", recovery);
    }
}
