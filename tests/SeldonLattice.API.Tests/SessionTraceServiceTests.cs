using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sentinel.Sdk.Observability;
using SeldonLattice.API.Services;
using SeldonLattice.API.Services.Outbox;
using SeldonLattice.Domain.Models;
using SeldonLattice.Domain.Models.Storage;
using Xunit;

namespace SeldonLattice.API.Tests;

/// <summary>
/// Unit tests for <see cref="SessionTraceService"/> - verifies that child spans
/// are created per debate turn, costs/tokens are aggregated, and deterministic
/// IDs are used for idempotent retries.
/// </summary>
public class SessionTraceServiceTests
{
    private static readonly SentinelConfiguration DefaultConfig = new()
    {
        Enabled = true,
        Tracing = new SentinelTracingConfig { Enabled = true },
        Pricing = new SentinelPricingConfig { InputPer1K = 0.0001m, OutputPer1K = 0.0003m },
        Agent = new SentinelAgentConfig { ExternalId = "seldon-lattice" },
    };

    private static StubOutbox CreateStubOutbox() => new();

    private static SentinelIntegrationService CreateStubIntegration() =>
        new(null!, Options.Create(DefaultConfig), NullLogger<SentinelIntegrationService>.Instance);

    private static SessionTraceService CreateService(ISentinelTraceOutbox outbox) =>
        new(outbox, CreateStubIntegration(), Options.Create(DefaultConfig), NullLogger<SessionTraceService>.Instance);

    [Fact]
    public async Task IngestSessionAsync_WithTurns_CreatesChildSpans()
    {
        var outbox = CreateStubOutbox();
        var service = CreateService(outbox);
        var turns = new List<DebateTurnEvent>
        {
            new() { Type = "proposer_reply", Turn = 0, Level = 1, SpeakerId = "proposer", Content = "Thesis", DialecticRole = DialecticRole.Thesis, InputTokens = 100, OutputTokens = 50 },
            new() { Type = "skeptic_reply", Turn = 0, Level = 1, SpeakerId = "skeptic", Content = "Antithesis", DialecticRole = DialecticRole.Antithesis, InputTokens = 120, OutputTokens = 60 },
            new() { Type = "synthesis", Turn = -1, Level = 1, SpeakerId = "mediator", Metaphor = "Synthesis", DialecticRole = DialecticRole.Synthesis },
        };

        await service.IngestSessionAsync("session-1", "philosophy", "What is philosophy?", null, turns, 0, 0, "");

        Assert.Single(outbox.ReceivedTraces);
        var trace = outbox.ReceivedTraces[0];
        Assert.Equal(3, trace.Children.Count);
        Assert.Equal("dialectic.proposer_reply:proposer", trace.Children[0].OperationName);
        Assert.Equal("dialectic.skeptic_reply:skeptic", trace.Children[1].OperationName);
        Assert.Equal("dialectic.synthesis:mediator", trace.Children[2].OperationName);
    }

    [Fact]
    public async Task IngestSessionAsync_WithTurns_AggregatesChildCostsIntoRoot()
    {
        var outbox = CreateStubOutbox();
        var service = CreateService(outbox);
        var turns = new List<DebateTurnEvent>
        {
            new() { Type = "proposer_reply", Turn = 0, Level = 1, SpeakerId = "proposer", InputTokens = 1000, OutputTokens = 500 },
            new() { Type = "skeptic_reply", Turn = 0, Level = 1, SpeakerId = "skeptic", InputTokens = 2000, OutputTokens = 1000 },
        };

        await service.IngestSessionAsync("session-2", "philosophy", "What is philosophy?", null, turns, 0, 0, "");

        var trace = outbox.ReceivedTraces[0];
        // Root cost = sum of child costs (1000/1000*0.0001 + 500/1000*0.0003 + 2000/1000*0.0001 + 1000/1000*0.0003)
        Assert.True(trace.Cost > 0);
        Assert.Equal(3000, trace.TokenUsage!.InputTokens);
        Assert.Equal(1500, trace.TokenUsage.OutputTokens);
    }

    [Fact]
    public async Task IngestSessionAsync_WithTurns_ChildCostsAreComputed()
    {
        var outbox = CreateStubOutbox();
        var service = CreateService(outbox);
        var turns = new List<DebateTurnEvent>
        {
            new() { Type = "proposer_reply", Turn = 0, Level = 1, SpeakerId = "proposer", InputTokens = 1000, OutputTokens = 500 },
        };

        await service.IngestSessionAsync("session-3", "philosophy", "What is philosophy?", null, turns, 0, 0, "");

        var trace = outbox.ReceivedTraces[0];
        var child = trace.Children[0];
        Assert.True(child.Cost > 0);
        Assert.Equal(1000, child.TokenUsage!.InputTokens);
        Assert.Equal(500, child.TokenUsage.OutputTokens);
    }

    [Fact]
    public async Task IngestSessionAsync_WithTurns_DeterministicChildIds()
    {
        var outbox = CreateStubOutbox();
        var service = CreateService(outbox);
        var turns = new List<DebateTurnEvent>
        {
            new() { Type = "proposer_reply", Turn = 0, Level = 1, SpeakerId = "proposer", InputTokens = 100, OutputTokens = 50 },
            new() { Type = "skeptic_reply", Turn = 0, Level = 1, SpeakerId = "skeptic", InputTokens = 100, OutputTokens = 50 },
        };

        await service.IngestSessionAsync("session-4", "philosophy", "What is philosophy?", null, turns, 0, 0, "");

        var trace = outbox.ReceivedTraces[0];
        // Same session + turn + type + speaker = same deterministic ID
        Assert.False(string.IsNullOrEmpty(trace.Children[0].Id));
        Assert.NotEqual(trace.Children[0].Id, trace.Children[1].Id);
    }

    [Fact]
    public async Task IngestSessionAsync_WithEmptyTurns_CreatesRootOnly()
    {
        var outbox = CreateStubOutbox();
        var service = CreateService(outbox);
        var result = new DialogueLevelResult { Level = 1, SynthesisHash = "abc", Metaphor = "test", NextTheme = "next", ConnectiveSentence = "conn", AscensionValidated = true, AscensionAttempts = 1, SynthesisThesis = "thesis", ModeratorSelectionConfidence = 0.9f, IsSustained = false };

        await service.IngestSessionResultAsync("session-5", "philosophy", "What is philosophy?", result, 0, 0, "");

        var trace = outbox.ReceivedTraces[0];
        Assert.Empty(trace.Children);
        Assert.Equal("dialectical-ascent", trace.OperationName);
    }

    [Fact]
    public async Task IngestSessionAsync_WithNullConfig_DoesNotThrow()
    {
        var outbox = CreateStubOutbox();
        var disabledConfig = new SentinelConfiguration { Enabled = false, Tracing = new SentinelTracingConfig { Enabled = false } };
        var service = new SessionTraceService(outbox, CreateStubIntegration(), Options.Create(disabledConfig), NullLogger<SessionTraceService>.Instance);
        var turns = new List<DebateTurnEvent> { new() { Type = "proposer_reply", Turn = 0, Level = 1, SpeakerId = "proposer" } };

        await service.IngestSessionAsync("session-6", "philosophy", "What is philosophy?", null, turns, 0, 0, "");

        Assert.Empty(outbox.ReceivedTraces);
    }

    [Fact]
    public async Task IngestSessionAsync_ChildSpansHaveParentTraceIdSet()
    {
        var outbox = CreateStubOutbox();
        var service = CreateService(outbox);
        var turns = new List<DebateTurnEvent>
        {
            new() { Type = "proposer_reply", Turn = 0, Level = 1, SpeakerId = "proposer", InputTokens = 100, OutputTokens = 50 },
        };

        await service.IngestSessionAsync("session-7", "philosophy", "What is philosophy?", null, turns, 0, 0, "");

        var trace = outbox.ReceivedTraces[0];
        Assert.Equal(trace.Id, trace.Children[0].ParentTraceId);
    }

    [Fact]
    public async Task IngestSessionAsync_TurnStatus_MappedCorrectly()
    {
        var outbox = CreateStubOutbox();
        var service = CreateService(outbox);
        var turns = new List<DebateTurnEvent>
        {
            new() { Type = "proposer_reply", Turn = 0, Level = 1, SpeakerId = "proposer", IsSecurityFlagged = true },
            new() { Type = "skeptic_reply", Turn = 0, Level = 1, SpeakerId = "skeptic", ExitReason = "QualityGate" },
            new() { Type = "synthesis", Turn = -1, Level = 1, SpeakerId = "mediator" },
        };

        await service.IngestSessionAsync("session-8", "philosophy", "What is philosophy?", null, turns, 0, 0, "");

        var trace = outbox.ReceivedTraces[0];
        Assert.Equal(TraceStatus.Error, trace.Children[0].Status);
        Assert.Equal(TraceStatus.Degraded, trace.Children[1].Status);
        Assert.Equal(TraceStatus.Ok, trace.Children[2].Status);
    }

    //  Stubs 

    private sealed class StubOutbox : ISentinelTraceOutbox
    {
        public List<SentinelTrace> ReceivedTraces { get; } = [];

        public Task EnqueueAsync(SentinelTrace trace, string sessionId, CancellationToken ct = default)
        {
            ReceivedTraces.Add(trace);
            return Task.CompletedTask;
        }

        public Task<int> FlushDueAsync(CancellationToken ct = default) => Task.FromResult(0);

        public Task<int> CountPendingAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
