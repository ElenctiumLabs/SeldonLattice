using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Application.Agents;
using SeldonLattice.Application.Agents.Moderators;
using SeldonLattice.Domain.Models.Agents;
using SeldonLattice.Domain.Options;
using Xunit;

namespace SeldonLattice.Application.Tests;

/// <summary>
/// The synthesis flow prepends the grounded-claims mandate onto every
/// moderator synthesis request, and the existing JSON parser tolerates the extra
/// claims/self_check/remaining_dissensus keys so the promise of structured, refutable
/// reasoning costs nothing today and feeds the ArgumentClaimNode ledger later.
/// </summary>
[Collection(PromptContractCollection.Name)]
public class SynthesisClaimsContractTests
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
            dialectician1Name: "Proposer", dialectician2Name: "Skeptic", dialogueLabel: "philosophical");
        return (agent, service);
    }

    private static ChatHistory BuildSynthesisHistory()
    {
        var agent = new MediatorAgent(
            Kernel.CreateBuilder().Build(),
            Options.Create(new DialecticsOptions()),
            new NoOpProviderRateLimiter(),
            dialectician1Name: "Proposer", dialectician2Name: "Skeptic", dialogueLabel: "philosophical");
        return agent.CreateSynthesisHistory(
            "What is philosophy?",
            [new AgentResponse("Fairness requires consistency"), new AgentResponse("Liberty constrains redistribution")],
            "philosophical tension analysis");
    }

    [Fact]
    public async Task SynthesizeAsync_PrependsClaimsContract_WhenEnabled()
    {
        var original = PromptContracts.Enabled;
        try
        {
            PromptContracts.Enabled = true;
            var (agent, service) = BuildAgent();

            var result = await agent.SynthesizeAsync(BuildSynthesisHistory());

            var prompt = Assert.Single(service.PromptsSeen);
            Assert.StartsWith("PARSING CONTRACT", prompt);
            Assert.Contains("MANDATE - GROUNDED SYNTHESIS", prompt);
            Assert.Contains("falsified_by", prompt);
            Assert.Contains("repeats_premise", prompt);
            Assert.Contains("[ABSTAIN]", prompt);
            Assert.Equal("Thesis from stub service.", result.Thesis);
            Assert.Equal("Does the martingale assumption hold across regimes?", result.NextTheme);
        }
        finally
        {
            PromptContracts.Enabled = original;
        }
    }

    [Fact]
    public async Task SynthesizeAsync_ClaimsRichJson_ParsesCoreFields()
    {
        var (agent, service) = BuildAgent();
        service.Responses["thesis from stub service"] = CapturingChatCompletionService.DefaultSynthesisJson;

        var result = await agent.SynthesizeAsync(BuildSynthesisHistory());

        Assert.Equal("probability", result.ImageDomain);
        Assert.StartsWith("The market is a martingale", result.Metaphor);
        Assert.Equal("Does the martingale assumption hold across regimes?", result.NextTheme);
        Assert.Equal("This generalizes the root because it asks whether stochastic fairness is global.", result.ConnectiveSentence);
    }

    [Fact]
    public async Task SynthesizeAsync_Disabled_SkipsClaimsContract()
    {
        var original = PromptContracts.Enabled;
        try
        {
            PromptContracts.Enabled = false;
            var agent = new MediatorAgent(
                Kernel.CreateBuilder().Build(),
                Options.Create(new DialecticsOptions()),
                new NoOpProviderRateLimiter(),
                dialectician1Name: "Proposer", dialectician2Name: "Skeptic", dialogueLabel: "philosophical");

            var history = agent.CreateSynthesisHistory(
                "What is precedent?",
                [new AgentResponse("test")],
                "philosophical analysis");

            // With the contract disabled, index 0 must remain the role content unmodified.
            Assert.DoesNotContain("MANDATE - GROUNDED SYNTHESIS", history[0].Content);
            Assert.DoesNotContain("PARSING CONTRACT", history[0].Content);
            Assert.Contains("OUTPUT: JSON", history[0].Content);
        }
        finally
        {
            PromptContracts.Enabled = original;
        }
    }

    [Fact]
    public void ReasonableSynthesisContract_CarriesAllMandateMarkers()
    {
        var contract = PromptContracts.ReasonableSynthesisContract;

        Assert.Contains("MANDATE - GROUNDED SYNTHESIS", contract);
        Assert.Contains("support_from", contract);
        Assert.Contains("attacked_by", contract);
        Assert.Contains("falsified_by", contract);
        Assert.Contains("confidence", contract);
        Assert.Contains("repeats_premise", contract);
        Assert.Contains("remaining_dissensus", contract);
        Assert.Contains("[ABSTAIN]", contract);
        Assert.Contains("0.3", contract);   // repeats_premise rejection threshold
        Assert.Contains("at least 3 claims", contract);
    }
}