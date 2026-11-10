using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using SeldonLattice.Application.Agents;
using SeldonLattice.Application.Agents.Dialecticians.Philosophy;
using SeldonLattice.Application.Agents.Moderators;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Options;
using Xunit;

namespace SeldonLattice.Application.Tests;

/// <summary>
/// Verifies the parse-awareness contract is injected at the very top of every agent
/// prompt, before any role text, so models of any size learn up front that their
/// reply is machine-parsed and how it is decoded.
/// </summary>
[Collection(PromptContractCollection.Name)]
public class PromptContractTests
{
    private static Kernel CreateKernel() => Kernel.CreateBuilder().Build();

    private static IOptions<DialecticsOptions> CreateOptions() => Options.Create(new DialecticsOptions());

    private static IProviderRateLimiter RateLimiter() => new NoOpProviderRateLimiter();

    [Fact]
    public void DialecticianPrompt_PlacesParsingContractAtTop()
    {
        var agent = new ProposerAgent(CreateKernel(), CreateOptions().Value, RateLimiter());

        var history = agent.CreateHistory("What is freedom?", rootTheme: "What is freedom?");
        var systemMessage = history[0].Content ?? "";

        Assert.StartsWith("PARSING CONTRACT", systemMessage);
        Assert.Contains("[REASONING]", systemMessage);
        Assert.Contains("[RESPONSE]", systemMessage);
        Assert.Contains("CORRECT EXAMPLE", systemMessage);
        Assert.Contains("write ONLY your public speech", systemMessage);
        Assert.True(systemMessage.IndexOf("PARSING CONTRACT", StringComparison.Ordinal) <
                    systemMessage.IndexOf("You are the Proposer", StringComparison.Ordinal));
    }

    [Fact]
    public void DialecticianPrompt_EnhancedIntensity_StillCarriesParsingContract()
    {
        var agent = new ProposerAgent(CreateKernel(), CreateOptions().Value, RateLimiter());

        var history = agent.CreateHistory("What is freedom?", intensity: 20);
        var systemMessage = history[0].Content ?? "";

        Assert.StartsWith("PARSING CONTRACT", systemMessage);
        Assert.Contains("[REASONING]", systemMessage);
        Assert.True(systemMessage.IndexOf("PARSING CONTRACT", StringComparison.Ordinal) <
                    systemMessage.IndexOf("You are the Proposer", StringComparison.Ordinal));
    }

    [Fact]
    public void MediatorPrompt_PlacesParsingContractAtTop()
    {
        var agent = new MediatorAgent(CreateKernel(),
            Options.Create(new DialecticsOptions()), RateLimiter(),
            dialectician1Name: "Proposer", dialectician2Name: "Skeptic", dialogueLabel: "philosophical");

        var history = agent.CreateSynthesisHistory("What is freedom?",
            previousResponses: [], moderationFocus: "balance");
        var systemMessage = history[0].Content ?? "";

        // The parsing contract is prepended at synthesis-call time (ModeratorBase.SynthesizeAsync),
        // so the role's own prompt must be bare and must demand the JSON output the parser reads.
        Assert.StartsWith("You are the Mediator", systemMessage);
        Assert.Contains("OUTPUT: JSON", systemMessage);
        Assert.Contains("image_domain", systemMessage);
    }

    [Fact]
    public void IsLikelyProseOnlyModel_DetectsKnownSmallLocalModels()
    {
        Assert.True(PromptContracts.IsLikelyProseOnlyModel("mistral-7b-instruct-v0.3"));
        Assert.True(PromptContracts.IsLikelyProseOnlyModel("MISTRAL-8x7B-instruct-v0.1"));
        Assert.False(PromptContracts.IsLikelyProseOnlyModel("gpt-4o"));
        Assert.False(PromptContracts.IsLikelyProseOnlyModel("llama-3.3-70b-versatile"));
        Assert.False(PromptContracts.IsLikelyProseOnlyModel(null));
        Assert.False(PromptContracts.IsLikelyProseOnlyModel(""));
        Assert.False(PromptContracts.IsLikelyProseOnlyModel("   "));
    }

    [Fact]
    public void Disabled_SkipsParsingContractEntirely()
    {
        var original = PromptContracts.Enabled;
        try
        {
            PromptContracts.Enabled = false;
            var agent = new ProposerAgent(CreateKernel(), CreateOptions().Value, RateLimiter());

            var history = agent.CreateHistory("What is freedom?");
            var systemMessage = history[0].Content ?? "";

            Assert.DoesNotContain("PARSING CONTRACT", systemMessage);
            Assert.StartsWith("You are the Proposer", systemMessage.TrimStart());
            Assert.Contains("[REASONING]", systemMessage); // role-own OUTPUT FORMAT block remains
        }
        finally
        {
            PromptContracts.Enabled = original;
        }
    }
}
