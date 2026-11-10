using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Application.Agents.Moderators;
using SeldonLattice.Domain.Options;
using Xunit;

namespace SeldonLattice.Application.Tests.Moderators;

/// <summary>
/// The ascension gate's view of the ladder.
///
/// The gate judged only whether a candidate generalised the root, so a restatement of an earlier
/// rung - which is still a valid generalisation of the root - passed every time. The circling in the
/// 2026-11-09 batch was exactly that: semantically identical rungs with no shared vocabulary, which
/// the gate had no way to see.
///
/// These tests pin that the gate prompt now carries the rungs and the second judgement count, and
/// that the JSON example survives <see cref="string.Format"/> - the prompt is a format template, so
/// the braces in the example must be doubled in source or the call throws.
/// </summary>
public class AscensionGatePromptTests
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

    private const string VerdictReply = """{"valid": true, "reasoning": "It stays on the ladder."}""";

    [Fact]
    public async Task GatePrompt_ContainsClimbedRungs()
    {
        var (agent, service) = BuildAgent();
        service.Responses["gatekeeper of the dialectical ladder"] = VerdictReply;

        await agent.EvaluateAscensionAsync(
            rootTheme: "What is the ground of anything?",
            candidateTheme: "What makes a reason sufficient?",
            connectiveSentence: "It asks what a reason must rest on.",
            climbedThemes: ["What is the ground of explanation?", "What makes a claim true?"]);

        var prompt = Assert.Single(service.PromptsSeen);

        Assert.Contains("Rung 1: What is the ground of explanation?", prompt);
        Assert.Contains("Rung 2: What makes a claim true?", prompt);
    }

    [Fact]
    public async Task GatePrompt_StatesBothJudgementCounts()
    {
        var (agent, service) = BuildAgent();
        service.Responses["gatekeeper of the dialectical ladder"] = VerdictReply;

        await agent.EvaluateAscensionAsync("root", "candidate", "connective",
            climbedThemes: ["an earlier rung"]);

        var prompt = Assert.Single(service.PromptsSeen);

        Assert.Contains("Root connection", prompt);
        Assert.Contains("Distinction", prompt);
        Assert.Contains("semantic restatement counts as a repeat", prompt);
    }

    [Fact]
    public async Task GatePrompt_WithoutRungs_ShowsPlaceholder()
    {
        var (agent, service) = BuildAgent();
        service.Responses["gatekeeper of the dialectical ladder"] = VerdictReply;

        await agent.EvaluateAscensionAsync("root", "candidate", "connective");

        var prompt = Assert.Single(service.PromptsSeen);

        Assert.Contains("(none yet)", prompt);
    }

    [Fact]
    public async Task GatePrompt_RendersTheJsonExampleWithSingleBraces()
    {
        // AscensionGatePrompt is passed through string.Format, so a single '{' in the source would
        // throw FormatException before the prompt ever reaches the model.
        var (agent, service) = BuildAgent();
        service.Responses["gatekeeper of the dialectical ladder"] = VerdictReply;

        var verdict = await agent.EvaluateAscensionAsync("root", "candidate", "connective",
            climbedThemes: ["an earlier rung"]);

        var prompt = Assert.Single(service.PromptsSeen);

        Assert.Contains("""{"valid": true or false""", prompt);
        Assert.True(verdict.IsValidGeneralization);
    }
}
