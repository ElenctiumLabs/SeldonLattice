using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Infrastructure.Audit;
using Xunit;

namespace SeldonLattice.Application.Tests.Audit;

public class AuditDelegatingChatClientTests
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
            yield return new ChatResponseUpdate { Contents = [new TextContent(res.Text)], ModelId = res.ModelId };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private class InMemoryMerkleStore : IMerkleStore
    {
        public List<LlmAuditNode> StoredAuditNodes { get; } = [];
        public bool ShouldThrowOnPut { get; init; }

        public Task<Cid> PutBlobAsync(IMerkleNode node)
        {
            if (ShouldThrowOnPut)
                throw new InvalidOperationException("Simulated storage write failure");

            if (node is LlmAuditNode auditNode)
                StoredAuditNodes.Add(auditNode);

            return Task.FromResult(node.GetCid());
        }

        public Task<T> GetBlobAsync<T>(Cid cid) where T : IMerkleNode => throw new NotImplementedException();
        public Task<Cid> PutAsync<T>(T obj) => throw new NotImplementedException();
        public Task<T> GetAsync<T>(Cid cid) => throw new NotImplementedException();
        public Task<Cid?> GetHeadAsync(string userId) => Task.FromResult<Cid?>(null);
        public Task UpdateHeadAsync(string userId, Cid commitCid) => Task.CompletedTask;
        public Task<Cid> CreateCommitAsync(string userId, string message, StateManifest manifest) => throw new NotImplementedException();
        public Task<LatticeCommit[]> GetHistoryAsync(string userId, int? limit = null) => Task.FromResult(Array.Empty<LatticeCommit>());
        public Task<IEnumerable<T>> GetAllNodesAsync<T>() where T : IMerkleNode => Task.FromResult(Enumerable.Empty<T>());
        public Task<StoreScan<T>> ScanBlobsAsync<T>(CancellationToken ct = default) where T : IMerkleNode
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<T>> GetAllAsync<T>() => Task.FromResult(Enumerable.Empty<T>());
        public Task PutSignatureAsync(AgentSignature signature) => Task.CompletedTask;
        public Task<AgentSignature?> GetSignatureAsync(Cid signedCid) => Task.FromResult<AgentSignature?>(null);
        public Task ClearAsync() => Task.CompletedTask;

        public async IAsyncEnumerable<LlmAuditNode> GetAuditNodesAsync(string sessionId)
        {
            foreach (var node in StoredAuditNodes.Where(n => n.SessionId == sessionId))
                yield return node;
        }
    }

    [Fact]
    public async Task GetResponseAsync_WritesAuditNodeToMerkleStore()
    {
        var store = new InMemoryMerkleStore();
        var innerClient = new FakeChatClient((msgs, opts, ct) => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Test completion")])
        {
            ModelId = "gpt-4o",
            Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 }
        }));

        var auditClient = new AuditDelegatingChatClient(
            innerClient,
            store,
            NullLogger<AuditDelegatingChatClient>.Instance,
            new AuditMiddlewareOptions());

        var auditContext = new AuditContext
        {
            SessionId = "sess-1",
            SpeakerId = "proposer",
            ModuleKey = "philosophy",
            Level = 1,
            Turn = 2
        };

        var options = auditContext.ToOptions();
        var response = await auditClient.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")], options);

        Assert.Equal("Test completion", response.Text);
        Assert.Single(store.StoredAuditNodes);

        var saved = store.StoredAuditNodes[0];
        Assert.Equal("sess-1", saved.SessionId);
        Assert.Equal("proposer", saved.SpeakerId);
        Assert.Equal("philosophy", saved.ModuleKey);
        Assert.Equal(1, saved.Level);
        Assert.Equal(2, saved.Turn);
        Assert.Equal("Test completion", saved.CompletionText);
        Assert.Equal(10, saved.InputTokenCount);
        Assert.Equal(5, saved.OutputTokenCount);
        Assert.Equal("gpt-4o", saved.ModelId);
        Assert.True(saved.LatencyMs >= 0);
    }

    [Fact]
    public async Task GetResponseAsync_OnInnerException_WritesFlaggedNodeAndRethrows()
    {
        var store = new InMemoryMerkleStore();
        var innerClient = new FakeChatClient((msgs, opts, ct) => throw new HttpRequestException("Network failure"));

        var auditClient = new AuditDelegatingChatClient(
            innerClient,
            store,
            NullLogger<AuditDelegatingChatClient>.Instance,
            new AuditMiddlewareOptions());

        var auditContext = new AuditContext
        {
            SessionId = "sess-err",
            SpeakerId = "mediator",
            ModuleKey = "philosophy"
        };

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            auditClient.GetResponseAsync([new ChatMessage(ChatRole.User, "Test")], auditContext.ToOptions()));

        Assert.Single(store.StoredAuditNodes);
        var node = store.StoredAuditNodes[0];
        Assert.True(node.IsFlagged);
        Assert.Contains("llm_error:HttpRequestException", node.QualityFlags);
    }

    [Fact]
    public async Task GetResponseAsync_OnStoreWriteFailure_DoesNotThrowAndReturnsCompletion()
    {
        var store = new InMemoryMerkleStore { ShouldThrowOnPut = true };
        var innerClient = new FakeChatClient((msgs, opts, ct) => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Success response")])));

        var auditClient = new AuditDelegatingChatClient(
            innerClient,
            store,
            NullLogger<AuditDelegatingChatClient>.Instance,
            new AuditMiddlewareOptions());

        var response = await auditClient.GetResponseAsync([new ChatMessage(ChatRole.User, "Test")]);

        Assert.NotNull(response);
        Assert.Equal("Success response", response.Text);
    }

    [Fact]
    public async Task GetResponseAsync_FlaggedOnlyRetention_SkipsUnflaggedNodes()
    {
        var store = new InMemoryMerkleStore();
        var innerClient = new FakeChatClient((msgs, opts, ct) => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Unflagged response")])));

        var auditClient = new AuditDelegatingChatClient(
            innerClient,
            store,
            NullLogger<AuditDelegatingChatClient>.Instance,
            new AuditMiddlewareOptions { AuditNodeRetentionPolicy = "flagged_only" });

        var ctx = new AuditContext { SessionId = "sess-retention", IsFlagged = false };
        await auditClient.GetResponseAsync([new ChatMessage(ChatRole.User, "Test")], ctx.ToOptions());

        Assert.Empty(store.StoredAuditNodes);

        // When flagged, it should write
        ctx.IsFlagged = true;
        await auditClient.GetResponseAsync([new ChatMessage(ChatRole.User, "Test 2")], ctx.ToOptions());
        Assert.Single(store.StoredAuditNodes);
    }
}
