using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using SeldonLattice.Application.Services;
using SeldonLattice.Domain.Options;
using SeldonLattice.Infrastructure.Audit;
using Xunit;

namespace SeldonLattice.Application.Tests.Audit;

public class QualityGateDelegatingChatClientTests
{
    private class FakeChatClient(Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> handler) : IChatClient
    {
        public ChatClientMetadata Metadata { get; } = new("fake");

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => handler(chatMessages, options, cancellationToken);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var res = await handler(chatMessages, options, cancellationToken);
            yield return new ChatResponseUpdate { Contents = [new TextContent(res.Text)] };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task QualityGate_DetectsCircularReasoning_WhenSimilarityExceedsThreshold()
    {
        var text1 = "The fundamental nature of justice is harmony between the rational, spirited, and appetitive parts of the human soul.";
        var text2 = "The fundamental nature of justice is harmony between the rational, spirited, and appetitive parts of the individual soul.";

        var currentText = text2;
        var innerClient = new FakeChatClient((msgs, opts, ct) => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, currentText)])));

        var qualityClient = new QualityGateDelegatingChatClient(
            innerClient,
            NullLogger<QualityGateDelegatingChatClient>.Instance,
            new AuditMiddlewareOptions { CircularReasoningThreshold = 0.55f });

        var ctx = new AuditContext();
        ctx.RecentCompletions.Enqueue(text1);

        await qualityClient.GetResponseAsync([new ChatMessage(ChatRole.User, "What is philosophy?")], ctx.ToOptions());

        Assert.True(ctx.IsFlagged);
        Assert.Contains("circular_reasoning", ctx.QualityFlags);
    }

    [Fact]
    public async Task QualityGate_DoesNotFlag_WhenBelowThreshold()
    {
        var text1 = "The concept of economic utility is determined by subjective consumer preferences.";
        var text2 = "Quantum electrodynamics describes how light and matter interact at subatomic scales.";

        var innerClient = new FakeChatClient((msgs, opts, ct) => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, text2)])));

        var qualityClient = new QualityGateDelegatingChatClient(
            innerClient,
            NullLogger<QualityGateDelegatingChatClient>.Instance,
            new AuditMiddlewareOptions { CircularReasoningThreshold = 0.55f });

        var ctx = new AuditContext();
        ctx.RecentCompletions.Enqueue(text1);

        await qualityClient.GetResponseAsync([new ChatMessage(ChatRole.User, "Explain physics")], ctx.ToOptions());

        Assert.False(ctx.IsFlagged);
        Assert.DoesNotContain("circular_reasoning", ctx.QualityFlags);
    }

    [Fact]
    public async Task QualityGate_SkipsCheck_WhenRecentCompletionsEmpty()
    {
        var text = "First response in session.";
        var innerClient = new FakeChatClient((msgs, opts, ct) => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, text)])));

        var qualityClient = new QualityGateDelegatingChatClient(
            innerClient,
            NullLogger<QualityGateDelegatingChatClient>.Instance,
            new AuditMiddlewareOptions());

        var ctx = new AuditContext();
        await qualityClient.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], ctx.ToOptions());

        Assert.False(ctx.IsFlagged);
        Assert.DoesNotContain("circular_reasoning", ctx.QualityFlags);
        Assert.Single(ctx.RecentCompletions);
        Assert.Equal(text, ctx.RecentCompletions.Peek());
    }

    [Fact]
    public async Task QualityGate_DetectsCeremonialLanguage()
    {
        var ceremonialText = "My dear friend, I am deeply moved and profound gratitude fills my heart for this eternal beloved sacred dialogue.";
        var innerClient = new FakeChatClient((msgs, opts, ct) => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, ceremonialText)])));

        var qg = new DialogueQualityGates(new QualityGateOptions(), NullLogger<DialogueQualityGates>.Instance);

        var qualityClient = new QualityGateDelegatingChatClient(
            innerClient,
            NullLogger<QualityGateDelegatingChatClient>.Instance,
            new AuditMiddlewareOptions(),
            qualityGates: qg);

        var ctx = new AuditContext();
        await qualityClient.GetResponseAsync([new ChatMessage(ChatRole.User, "Greetings")], ctx.ToOptions());

        Assert.True(ctx.IsFlagged);
        Assert.Contains("ceremonial_language", ctx.QualityFlags);
    }

    [Fact]
    public void JaccardBigramSimilarity_CalculatesExpectedScore()
    {
        var a = "the quick brown fox";
        var b = "the quick brown fox";
        Assert.Equal(1.0f, QualityGateDelegatingChatClient.JaccardBigramSimilarity(a, b));

        var c = "the quick brown fox jumps";
        var d = "completely different words here now";
        Assert.Equal(0.0f, QualityGateDelegatingChatClient.JaccardBigramSimilarity(c, d));
    }
}
