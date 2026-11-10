using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SeldonLattice.Application.Services;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace SeldonLattice.Infrastructure.Audit;

/// <summary>
/// MEAI DelegatingChatClient middleware layer that performs quality gate analysis
/// (circular reasoning, ceremonial language) on completed LLM responses, attaching
/// findings as structured metadata to the current <see cref="AuditContext"/>.
/// </summary>
public class QualityGateDelegatingChatClient(
    IChatClient innerClient,
    ILogger<QualityGateDelegatingChatClient> logger,
    AuditMiddlewareOptions options,
    DialogueQualityGates? qualityGates = null,
    IServiceScopeFactory? scopeFactory = null)
    : DelegatingChatClient(innerClient)
{
    private readonly ILogger<QualityGateDelegatingChatClient> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly AuditMiddlewareOptions _options = options ?? new AuditMiddlewareOptions();
    private int _missingQualityGatesWarningLogged;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(chatMessages, options, cancellationToken);
        var ctx = ResolveContext(options);
        var text = response.Text ?? string.Empty;

        EvaluateQuality(text, ctx);

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var ctx = ResolveContext(options);
        var buffer = new StringBuilder();

        IAsyncEnumerator<ChatResponseUpdate> enumerator;
        try
        {
            enumerator = base.GetStreamingResponseAsync(chatMessages, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch
        {
            // Propagate without evaluating or modifying AuditContext
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
                catch
                {
                    // Stream terminated with an error; propagate without modifying AuditContext
                    throw;
                }

                if (!string.IsNullOrEmpty(update.Text))
                {
                    buffer.Append(update.Text);
                }

                yield return update;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        EvaluateQuality(buffer.ToString(), ctx);
    }

    private void EvaluateQuality(string text, AuditContext ctx)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        // 1. Analyze response via DialogueQualityGates if available
        DialogueQualityGates? qg = qualityGates;
        using var scope = qg == null && scopeFactory != null ? scopeFactory.CreateScope() : null;
        if (qg == null && scope != null)
        {
            qg = scope.ServiceProvider.GetService<DialogueQualityGates>();
        }

        if (qg != null)
        {
            var metrics = qg.AnalyzeResponse(text, null, 0);
            if (metrics.CeremonyScore > 1.0)
            {
                if (!ctx.QualityFlags.Contains("ceremonial_language"))
                    ctx.QualityFlags.Add("ceremonial_language");
            }
        }
        else if (System.Threading.Interlocked.Exchange(ref _missingQualityGatesWarningLogged, 1) == 0)
        {
            _logger.LogWarning(
                "Audit quality analysis is enabled but DialogueQualityGates could not be resolved; ceremony analysis will be skipped.");
        }

        // 2. Circular-reasoning check against recent completions
        if (ctx.RecentCompletions.Count >= 1)
        {
            foreach (var prior in ctx.RecentCompletions)
            {
                var similarity = JaccardBigramSimilarity(text, prior);
                if (similarity > _options.CircularReasoningThreshold)
                {
                    if (!ctx.QualityFlags.Contains("circular_reasoning"))
                        ctx.QualityFlags.Add("circular_reasoning");
                    break;
                }
            }
        }

        // 3. Update IsFlagged status
        ctx.IsFlagged = ctx.QualityFlags.Contains("circular_reasoning") ||
                        ctx.QualityFlags.Contains("ceremonial_language");

        if (ctx.IsFlagged)
        {
            _logger.LogWarning(
                "Quality violation detected for session {SessionId}, speaker {SpeakerId}, level {Level}, turn {Turn}: {Flags}",
                ctx.SessionId, ctx.SpeakerId, ctx.Level, ctx.Turn, string.Join(", ", ctx.QualityFlags));
        }

        // 4. Update recent completions sliding window (capacity 3)
        if (text.Length > 0)
        {
            if (ctx.RecentCompletions.Count >= 3)
            {
                ctx.RecentCompletions.Dequeue();
            }
            ctx.RecentCompletions.Enqueue(text);
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

    public static float JaccardBigramSimilarity(string a, string b)
    {
        var bigramsA = GetBigrams(a);
        var bigramsB = GetBigrams(b);
        var intersection = bigramsA.Intersect(bigramsB).Count();
        var union = bigramsA.Union(bigramsB).Count();
        return union == 0 ? 0f : (float)intersection / (float)union;
    }

    private static HashSet<string> GetBigrams(string text)
    {
        var words = text.Split(
            [' ', '\r', '\n', '\t', '.', ',', '!', '?', ';', ':'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < words.Length - 1; i++)
        {
            set.Add($"{words[i]} {words[i + 1]}");
        }
        return set;
    }
}
