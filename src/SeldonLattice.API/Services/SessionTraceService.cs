using Microsoft.Extensions.Options;
using Sentinel.Sdk.Observability;
using SeldonLattice.API.Services.Outbox;
using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.API.Services;

/// <summary>
/// Ingests dialectical reasoning sessions as Sentinel traces.
/// Each session-level ascent becomes a trace; each debate turn becomes a trace event;
/// synthesis metadata (metaphor, thesis, Merkle CIDs) rides on trace attributes so
/// Sentinel findings stay anchored to the tamper-evident evidence chain.
/// Delivery is at-least-once via the SQLite outbox: the trace is persisted before any
/// network attempt and retried with backoff if Sentinel is unreachable.
/// </summary>
public sealed class SessionTraceService(
    ISentinelTraceOutbox outbox,
    SentinelIntegrationService sentinelIntegration,
    IOptions<SentinelConfiguration> config,
    ILogger<SessionTraceService> logger)
{
    internal const string PROBE_CONST = " PROBE-XYZ-123 ";
    private readonly SentinelConfiguration _config = config.Value;

    /// <summary>
    /// Ingests a completed dialectical level (full turn transcript) as a Sentinel trace.
    /// </summary>
    public async Task IngestSessionAsync(
        string sessionId,
        string framework,
        string rootQuestion,
        DialogueLevelResult? result,
        IReadOnlyList<DebateTurnEvent> turns,
        int inputTokens,
        int outputTokens,
        string modelId,
        decimal? cost = null,
        CancellationToken ct = default)
    {
        if (!_config.Enabled || !_config.Tracing.Enabled)
            return;

        // Result-only exports (e.g. the streaming endpoint passes no turn
        // transcript) are valid: BuildTrace handles an empty turns list.
        if (turns.Count == 0 && result is null)
            return;

        try
        {
            var now = DateTime.UtcNow;
            var startedAt = turns.Count > 0 ? turns[0].Timestamp : now;
            var endedAt = turns.Count > 0 ? turns[^1].Timestamp : now;

            var trace = BuildTrace(sessionId, framework, rootQuestion, result, turns, startedAt, endedAt);
            if (!string.IsNullOrEmpty(modelId))
                trace.Attributes["model_name"] = modelId;
            // Only compute root-level cost from flat token counts if child spans
            // haven't already set it (child spans have more accurate per-turn data).
            if (trace.Cost is null && (_config.Pricing.InputPer1K > 0 || _config.Pricing.OutputPer1K > 0))
            {
                trace.Cost = Math.Max(0, inputTokens) / 1000m * _config.Pricing.InputPer1K
                       + Math.Max(0, outputTokens) / 1000m * _config.Pricing.OutputPer1K;
            }

            // Only set root token usage from flat params if child spans didn't
            // provide per-turn data (children are more accurate).
            if (trace.TokenUsage is null || (trace.TokenUsage.InputTokens == 0 && trace.TokenUsage.OutputTokens == 0))
            {
                trace.TokenUsage = new TokenUsage
                {
                    InputTokens = Math.Max(0, inputTokens),
                    OutputTokens = Math.Max(0, outputTokens),
                };
            }
            if (cost.HasValue)
                trace.Cost = cost.Value;

            // Persist first (at-least-once), then attempt immediate delivery; the
            // outbox flush worker retries any failures with exponential backoff.
            await outbox.EnqueueAsync(trace, sessionId, ct);
            var delivered = await outbox.FlushDueAsync(ct);

            if (delivered > 0)
                logger.LogInformation("Sentinel trace delivered for session {SessionId} ({TurnCount} turns)",
                    sessionId, turns.Count);
            else
                logger.LogWarning("Sentinel unreachable - trace for session {SessionId} queued in outbox for retry",
                    sessionId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Enqueue itself failed (disk/EF error). Nothing durable captured the
            // trace, so surface loudly but never break the reasoning run.
            logger.LogError(ex, "Failed to queue Sentinel trace export for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Ingests a completed dialectical level when only the aggregate result is available
    /// (blocking ascend path does not expose per-turn events).
    /// </summary>
    public Task IngestSessionResultAsync(
        string sessionId,
        string framework,
        string rootQuestion,
        DialogueLevelResult result,
        int inputTokens,
        int outputTokens,
        string modelId,
        CancellationToken ct = default)
        => IngestSessionAsync(sessionId, framework, rootQuestion, result, [], inputTokens, outputTokens, modelId, ct: ct);

    private SentinelTrace BuildTrace(
        string sessionId,
        string framework,
        string rootQuestion,
        DialogueLevelResult? result,
        IReadOnlyList<DebateTurnEvent> turns,
        DateTime startedAt,
        DateTime endedAt)
    {
        var attributes = new Dictionary<string, object>
        {
            ["framework"] = framework,
            ["sessionId"] = sessionId,
            ["rootQuestion"] = Truncate(rootQuestion),
            ["turnCount"] = turns.Count,
            ["agentVersion"] = "1.0.0"
        };

        var synthesis = turns.LastOrDefault(t =>
            string.Equals(t.Type, "synthesis", StringComparison.OrdinalIgnoreCase));

        if (synthesis is not null)
        {
            attributes["metaphor"] = Truncate(synthesis.Metaphor);
            attributes["nextTheme"] = Truncate(synthesis.NextTheme);
            attributes["connectiveSentence"] = Truncate(synthesis.ConnectiveSentence);
            attributes["synthesisThesis"] = Truncate(synthesis.SynthesisThesis);
            if (synthesis.AscensionValidated.HasValue)
                attributes["ascensionValidated"] = synthesis.AscensionValidated.Value;
            if (synthesis.IsSustained.HasValue)
                attributes["isSustained"] = synthesis.IsSustained.Value;
            if (!string.IsNullOrWhiteSpace(synthesis.SustainedReason))
                attributes["sustainedReason"] = synthesis.SustainedReason;
        }

        if (!string.IsNullOrWhiteSpace(synthesis?.ExitReason))
            attributes["exitReason"] = synthesis!.ExitReason!;

        if (result is not null)
        {
            attributes["level"] = result.Level;
            attributes["synthesisHash"] = result.SynthesisHash;
            attributes["ascensionValidated"] = result.AscensionValidated;
            attributes["ascensionAttempts"] = result.AscensionAttempts;
            attributes["moderatorSelectionConfidence"] = result.ModeratorSelectionConfidence;
            attributes["isSustained"] = result.IsSustained;
        }
        else if (turns.Count > 0)
        {
            attributes["level"] = turns[^1].Level;
        }

        // Build child spans: one per debate turn, each with its own cost and token usage.
        var children = turns.Select(t => BuildChildSpan(t, sessionId)).ToList();

        // Root trace aggregates children's token usage and cost.
        long totalInput = children.Sum(c => c.TokenUsage?.InputTokens ?? 0);
        long totalOutput = children.Sum(c => c.TokenUsage?.OutputTokens ?? 0);
        decimal totalCost = children.Sum(c => c.Cost ?? 0m);

        var rootId = Guid.NewGuid().ToString();

        foreach (var child in children)
        {
            child.ParentTraceId = rootId;
        }

        return new SentinelTrace
        {
            Id = rootId,
            AgentId = sentinelIntegration.GetAgentIdAsync().GetAwaiter().GetResult() ?? _config.Agent.ExternalId,
            OperationName = "dialectical-ascent",
            OperationType = "reasoning",
            Status = TraceStatus.Ok,
            StartedAt = startedAt,
            EndedAt = endedAt,
            DurationMs = (long)(endedAt - startedAt).TotalMilliseconds,
            Attributes = attributes,
            TokenUsage = new TokenUsage
            {
                InputTokens = (int)Math.Max(0, totalInput),
                OutputTokens = (int)Math.Max(0, totalOutput),
            },
            Cost = totalCost > 0 ? totalCost : null,
            Children = children
        };
    }

    private SentinelTrace BuildChildSpan(DebateTurnEvent turn, string sessionId)
    {
        var operationName = string.IsNullOrWhiteSpace(turn.SpeakerId)
            ? $"dialectic.{turn.Type}"
            : $"dialectic.{turn.Type}:{turn.SpeakerId}";

        var inputTokens = Math.Max(0, (int)(turn.InputTokens ?? 0));
        var outputTokens = Math.Max(0, (int)(turn.OutputTokens ?? 0));

        decimal? cost = null;
        if (_config.Pricing.InputPer1K > 0 || _config.Pricing.OutputPer1K > 0)
        {
            cost = inputTokens / 1000m * _config.Pricing.InputPer1K
                 + outputTokens / 1000m * _config.Pricing.OutputPer1K;
        }

        var status = turn.IsSecurityFlagged ? TraceStatus.Error
            : turn.ExitReason != null ? TraceStatus.Degraded
            : TraceStatus.Ok;

        // Deterministic ID so outbox retries don't create duplicates.
        // Level is required: without it, turn 0 of level 1 collides with
        // turn 0 of level 0 and the upsert overwrites the earlier span.
        var childId = DeterministicId($"{sessionId}:{turn.Level}:{turn.Turn}:{turn.Type}:{turn.SpeakerId}");

        return new SentinelTrace
        {
            Id = childId,
            AgentId = sentinelIntegration.GetAgentIdAsync().GetAwaiter().GetResult() ?? _config.Agent.ExternalId,
            OperationName = operationName,
            OperationType = "reasoning",
            Status = status,
            StartedAt = turn.Timestamp,
            EndedAt = turn.Timestamp,
            DurationMs = 0,
            ModelName = turn.ModelId,
            Attributes = BuildTurnAttributes(turn),
            TokenUsage = new TokenUsage
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
            },
            Cost = cost,
            Children = []
        };
    }

    private static Dictionary<string, object> BuildTurnAttributes(DebateTurnEvent turn)
    {
        var attributes = new Dictionary<string, object>
        {
            ["eventType"] = turn.Type,
            ["turn"] = turn.Turn,
            ["dialecticRole"] = turn.DialecticRole.ToString(),
            ["qualityScore"] = turn.QualityScore,
            ["ceremonyScore"] = turn.CeremonyScore,
            ["similarityToPrevious"] = turn.SimilarityToPrevious,
            ["newArgumentCount"] = turn.NewArgumentCount,
            ["wordCount"] = turn.WordCount,
            ["hasFarewell"] = turn.HasFarewell,
            ["hasAgreement"] = turn.HasAgreement,
            ["hasDisagreement"] = turn.HasDisagreement
        };

        if (turn.Level > 0)
            attributes["level"] = turn.Level;
        if (!string.IsNullOrEmpty(turn.Phase))
            attributes["phase"] = turn.Phase!;
        if (turn.IsSecurityFlagged)
        {
            attributes["isSecurityFlagged"] = true;
            if (!string.IsNullOrEmpty(turn.SecurityThreatReason))
                attributes["securityThreatReason"] = turn.SecurityThreatReason!;
        }
        if (!string.IsNullOrEmpty(turn.ExitReason))
            attributes["exitReason"] = turn.ExitReason!;
        if (turn.TriggeringSignal.HasValue)
            attributes["triggeringSignal"] = turn.TriggeringSignal.Value.ToString();
        if (turn.AscensionValidated.HasValue)
            attributes["ascensionValidated"] = turn.AscensionValidated.Value;
        if (!string.IsNullOrEmpty(turn.Metaphor))
            attributes["metaphor"] = Truncate(turn.Metaphor);
        if (turn.InputTokens.HasValue)
            attributes["inputTokens"] = turn.InputTokens.Value;
        if (turn.OutputTokens.HasValue)
            attributes["outputTokens"] = turn.OutputTokens.Value;

        return attributes;
    }

    /// <summary>
    /// Produces a deterministic GUID from a key so outbox retries don't
    /// create duplicate child spans on re-ingestion of the same session.
    /// </summary>
    private static string DeterministicId(string key)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        var hex = Convert.ToHexString(hash).ToLowerInvariant().Replace("-", "");
        var guid = hex[..32];
        return $"{guid[..8]}-{guid[8..12]}-{guid[12..16]}-{guid[16..20]}-{guid[20..32]}";
    }

    /// <summary>Sentinel trace attributes are string-valued at the wire boundary.</summary>
    private static string Truncate(string? value, int maxLength = 512)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
