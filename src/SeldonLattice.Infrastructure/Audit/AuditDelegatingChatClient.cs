using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Infrastructure.Services;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace SeldonLattice.Infrastructure.Audit;

/// <summary>
/// MEAI DelegatingChatClient middleware layer that intercepts every LLM completion
/// across all modules and persists a signed <see cref="LlmAuditNode"/> into <see cref="IMerkleStore"/>.
/// Also records token usage into <see cref="ILlmUsageCollector"/> for Sentinel cost tracking
/// whenever an ambient <see cref="LlmUsageScope"/> is active.
/// </summary>
public class AuditDelegatingChatClient(
    IChatClient innerClient,
    IMerkleStore store,
    ILogger<AuditDelegatingChatClient> logger,
    AuditMiddlewareOptions options,
    AuditMetrics? metrics = null,
    ILlmUsageCollector? usageCollector = null)
    : DelegatingChatClient(innerClient)
{
    private readonly IMerkleStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly ILogger<AuditDelegatingChatClient> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly AuditMiddlewareOptions _options = options ?? new AuditMiddlewareOptions();

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = ResolveContext(options);
        var stopwatch = Stopwatch.StartNew();

        ChatResponse response;
        try
        {
            response = await base.GetResponseAsync(chatMessages, options, cancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var errorNode = new LlmAuditNode
            {
                NodeType = "llm_audit",
                SessionId = ctx.SessionId,
                SpeakerId = ctx.SpeakerId,
                ModuleKey = ctx.ModuleKey,
                Level = ctx.Level,
                Turn = ctx.Turn,
                PromptMessages = SummarizePromptMessages(chatMessages),
                CompletionText = string.Empty,
                InputTokenCount = 0,
                OutputTokenCount = 0,
                ModelId = options?.ModelId ?? string.Empty,
                LatencyMs = stopwatch.ElapsedMilliseconds,
                Timestamp = DateTime.UtcNow,
                ParentCid = ctx.ParentCid,
                QualityFlags = [$"llm_error:{ex.GetType().Name}"],
                IsFlagged = true
            };

            await TryWriteAuditNodeAsync(errorNode, ctx);
            throw;
        }

        stopwatch.Stop();
        var elapsed = stopwatch.ElapsedMilliseconds;

        var completionText = response.Text ?? string.Empty;
        var node = new LlmAuditNode
        {
            NodeType = "llm_audit",
            SessionId = ctx.SessionId,
            SpeakerId = ctx.SpeakerId,
            ModuleKey = ctx.ModuleKey,
            Level = ctx.Level,
            Turn = ctx.Turn,
            PromptMessages = SummarizePromptMessages(chatMessages),
            CompletionText = completionText,
            InputTokenCount = (int)(response.Usage?.InputTokenCount ?? 0),
            OutputTokenCount = (int)(response.Usage?.OutputTokenCount ?? 0),
            ModelId = response.ModelId ?? options?.ModelId ?? string.Empty,
            LatencyMs = elapsed,
            Timestamp = DateTime.UtcNow,
            ParentCid = ctx.ParentCid,
            QualityFlags = [.. ctx.QualityFlags],
            IsFlagged = ctx.IsFlagged
        };

        if (ShouldPersist(node))
        {
            await TryWriteAuditNodeAsync(node, ctx);
        }

        RecordUsage(node);

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var ctx = ResolveContext(options);
        var stopwatch = Stopwatch.StartNew();
        var buffer = new StringBuilder();
        string? modelId = null;
        long inputTokens = 0;
        long outputTokens = 0;

        IAsyncEnumerator<ChatResponseUpdate> enumerator;
        try
        {
            enumerator = base.GetStreamingResponseAsync(chatMessages, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var errorNode = new LlmAuditNode
            {
                NodeType = "llm_audit",
                SessionId = ctx.SessionId,
                SpeakerId = ctx.SpeakerId,
                ModuleKey = ctx.ModuleKey,
                Level = ctx.Level,
                Turn = ctx.Turn,
                PromptMessages = SummarizePromptMessages(chatMessages),
                CompletionText = string.Empty,
                InputTokenCount = 0,
                OutputTokenCount = 0,
                ModelId = options?.ModelId ?? string.Empty,
                LatencyMs = stopwatch.ElapsedMilliseconds,
                Timestamp = DateTime.UtcNow,
                ParentCid = ctx.ParentCid,
                QualityFlags = [$"llm_error:{ex.GetType().Name}"],
                IsFlagged = true
            };
            await TryWriteAuditNodeAsync(errorNode, ctx);
            throw;
        }

        try
        {
            while (true)
            {
                ChatResponseUpdate update;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                        break;
                    update = enumerator.Current;
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    var errorNode = new LlmAuditNode
                    {
                        NodeType = "llm_audit",
                        SessionId = ctx.SessionId,
                        SpeakerId = ctx.SpeakerId,
                        ModuleKey = ctx.ModuleKey,
                        Level = ctx.Level,
                        Turn = ctx.Turn,
                        PromptMessages = SummarizePromptMessages(chatMessages),
                        CompletionText = buffer.ToString(),
                        InputTokenCount = (int)inputTokens,
                        OutputTokenCount = (int)outputTokens,
                        ModelId = modelId ?? options?.ModelId ?? string.Empty,
                        LatencyMs = stopwatch.ElapsedMilliseconds,
                        Timestamp = DateTime.UtcNow,
                        ParentCid = ctx.ParentCid,
                        QualityFlags = [$"llm_error:{ex.GetType().Name}"],
                        IsFlagged = true
                    };
                    await TryWriteAuditNodeAsync(errorNode, ctx);
                    throw;
                }

                if (!string.IsNullOrEmpty(update.Text))
                {
                    buffer.Append(update.Text);
                }
                if (!string.IsNullOrEmpty(update.ModelId))
                {
                    modelId = update.ModelId;
                }
                if (update.Contents != null)
                {
                    foreach (var content in update.Contents)
                    {
                        if (content is UsageContent usage)
                        {
                            inputTokens = usage.Details.InputTokenCount ?? inputTokens;
                            outputTokens = usage.Details.OutputTokenCount ?? outputTokens;
                        }
                    }
                }

                yield return update;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        stopwatch.Stop();
        var elapsed = stopwatch.ElapsedMilliseconds;

        var node = new LlmAuditNode
        {
            NodeType = "llm_audit",
            SessionId = ctx.SessionId,
            SpeakerId = ctx.SpeakerId,
            ModuleKey = ctx.ModuleKey,
            Level = ctx.Level,
            Turn = ctx.Turn,
            PromptMessages = SummarizePromptMessages(chatMessages),
            CompletionText = buffer.ToString(),
            InputTokenCount = (int)inputTokens,
            OutputTokenCount = (int)outputTokens,
            ModelId = modelId ?? options?.ModelId ?? string.Empty,
            LatencyMs = elapsed,
            Timestamp = DateTime.UtcNow,
            ParentCid = ctx.ParentCid,
            QualityFlags = [.. ctx.QualityFlags],
            IsFlagged = ctx.IsFlagged
        };

        if (ShouldPersist(node))
        {
            await TryWriteAuditNodeAsync(node, ctx);
        }

        RecordUsage(node);
    }

    private void RecordUsage(LlmAuditNode node)
    {
        var sessionId = LlmUsageScope.CurrentSessionId;
        
        _logger.LogWarning("RecordUsage called. SessionId: '{SessionId}', Input: {Input}, Output: {Output}, HasCollector: {HasCollector}", 
            sessionId, node.InputTokenCount, node.OutputTokenCount, usageCollector != null);

        if (string.IsNullOrEmpty(sessionId) || usageCollector is null)
            return;

        try
        {
            usageCollector.Add(sessionId, node.InputTokenCount, node.OutputTokenCount, node.ModelId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Usage recording failed for session {SessionId}", sessionId);
        }
    }

    private static AuditContext ResolveContext(ChatOptions? options)
    {
        if (options?.AdditionalProperties != null &&
            options.AdditionalProperties.TryGetValue("audit_context", out var raw) &&
            raw is AuditContext ctx)
        {
            return ctx;
        }

        return AuditContext.Empty;
    }

    private bool ShouldPersist(LlmAuditNode node)
    {
        if (string.Equals(_options.AuditNodeRetentionPolicy, "flagged_only", StringComparison.OrdinalIgnoreCase))
        {
            return node.IsFlagged;
        }

        return true;
    }

    private async Task TryWriteAuditNodeAsync(LlmAuditNode node, AuditContext ctx)
    {
        try
        {
            var cid = await _store.PutBlobAsync(node);
            _logger.LogDebug(
                "AuditNode written for session {SessionId}, speaker {SpeakerId}, CID {Cid} in {LatencyMs}ms (Tokens: In={InputTokens}, Out={OutputTokens})",
                node.SessionId, node.SpeakerId, cid, node.LatencyMs, node.InputTokenCount, node.OutputTokenCount);

            metrics?.AuditNodeLatency.Record(
                node.LatencyMs,
                new KeyValuePair<string, object?>("speaker_id", ctx.SpeakerId),
                new KeyValuePair<string, object?>("module_key", ctx.ModuleKey));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "AuditNode write failed for session {SessionId}, speaker {SpeakerId}: {ExceptionType} - {Message}",
                ctx.SessionId, ctx.SpeakerId, ex.GetType().Name, ex.Message);

            metrics?.AuditWriteFailures.Add(1);
        }
    }

    private static string[] SummarizePromptMessages(IEnumerable<ChatMessage> chatMessages)
    {
        if (chatMessages == null)
            return [];

        var list = chatMessages.ToList();
        var summaries = new string[list.Count];
        for (var i = 0; i < list.Count; i++)
        {
            var msg = list[i];
            var role = msg.Role.Value;
            var text = msg.Text ?? string.Empty;
            var truncated = text.Length > 120 ? text[..120] + "…" : text;
            summaries[i] = $"{role}: {truncated}";
        }

        return summaries;
    }
}
