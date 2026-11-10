using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Application.Agents.Dialecticians;
using SeldonLattice.Application.Agents.Dialecticians.Philosophy;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Cycle;
using SeldonLattice.Domain.Options;
using Xunit;

namespace SeldonLattice.Application.Tests;

public class DialecticianBaseAffectTests
{
    private sealed class TestDialectician(Kernel kernel, NoOpProviderRateLimiter rateLimiter)
        : DialecticianBase(kernel, rateLimiter), IDialectician
    {
        public ChatHistory CreateHistory(string theme, string? rootTheme = null, RhetoricalMove? move = null, int intensity = -1)
            => [];
    }

    private static (Kernel, NoOpProviderRateLimiter) CreateDependencies()
    {
        var kernel = Kernel.CreateBuilder().Build();
        var rateLimiter = new NoOpProviderRateLimiter();
        return (kernel, rateLimiter);
    }

    [Fact]
    public void DialecticianBase_HighArousalNegativeValence_ReturnsPressureInstruction()
    {
        var (kernel, rateLimiter) = CreateDependencies();
        var agent = new TestDialectician(kernel, rateLimiter);

        var evl = new EmotionalVolatilityState
        {
            Arousal = 0.85,
            Valence = -0.5,
        };

        var instruction = agent.BuildAffectInstruction(evl);
        Assert.Contains("under genuine pressure", instruction);
    }

    [Fact]
    public void DialecticianBase_HighArousalPositiveValence_ReturnsMomentumInstruction()
    {
        var (kernel, rateLimiter) = CreateDependencies();
        var agent = new TestDialectician(kernel, rateLimiter);

        var evl = new EmotionalVolatilityState
        {
            Arousal = 0.85,
            Valence = 0.5,
        };

        var instruction = agent.BuildAffectInstruction(evl);
        Assert.Contains("argument is landing", instruction);
    }

    [Fact]
    public void DialecticianBase_LowArousal_ReturnsHeatRecoveryInstruction()
    {
        var (kernel, rateLimiter) = CreateDependencies();
        var agent = new TestDialectician(kernel, rateLimiter);

        var evl = new EmotionalVolatilityState
        {
            Arousal = 0.25,
            Valence = 0.0,
        };

        var instruction = agent.BuildAffectInstruction(evl);
        Assert.Contains("losing heat", instruction);
    }

    [Fact]
    public void DialecticianBase_NegativeMomentum_ReturnsConcretenessInstruction()
    {
        var (kernel, rateLimiter) = CreateDependencies();
        var agent = new TestDialectician(kernel, rateLimiter);

        var evl = new EmotionalVolatilityState
        {
            Arousal = 0.5,
            Momentum = -0.4,
        };

        var instruction = agent.BuildAffectInstruction(evl);
        Assert.Contains("trajectory is declining", instruction);
    }

    [Fact]
    public void DialecticianBase_HighProvocation_ReturnsDirectResponseInstruction()
    {
        var (kernel, rateLimiter) = CreateDependencies();
        var agent = new TestDialectician(kernel, rateLimiter);

        var evl = new EmotionalVolatilityState
        {
            Arousal = 0.5,
            Provocation = 0.8,
        };

        var instruction = agent.BuildAffectInstruction(evl);
        Assert.Contains("struck a real blow", instruction);
    }

    [Fact]
    public void DialecticianBase_BaselineState_ReturnsDefaultLiveInstruction()
    {
        var (kernel, rateLimiter) = CreateDependencies();
        var agent = new TestDialectician(kernel, rateLimiter);

        var instruction = agent.BuildAffectInstruction(EmotionalVolatilityState.Neutral);
        Assert.Contains("dialectic is live", instruction);
    }

    [Fact]
    public void SkepticAgent_OverridesAffectInstruction_WithRoleVocabulary()
    {
        var (kernel, rateLimiter) = CreateDependencies();
        var options = new DialecticsOptions();
        var agent = new SkepticAgent(kernel, options, rateLimiter);

        var evlHighNeg = new EmotionalVolatilityState { Arousal = 0.8, Valence = -0.4 };
        var phrase = agent.BuildAffectInstruction(evlHighNeg);
        Assert.Contains("concrete", phrase, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProposerAgent_OverridesAffectInstruction_WithRoleVocabulary()
    {
        var (kernel, rateLimiter) = CreateDependencies();
        var options = new DialecticsOptions();
        var agent = new ProposerAgent(kernel, options, rateLimiter);

        var evlHighNeg = new EmotionalVolatilityState { Arousal = 0.8, Valence = -0.4 };
        var phrase = agent.BuildAffectInstruction(evlHighNeg);
        Assert.Contains("elenchus", phrase, StringComparison.OrdinalIgnoreCase);
    }
}
