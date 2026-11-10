using System.Collections.Concurrent;

namespace SeldonLattice.Infrastructure.Services;

/// <summary>
/// Accumulates OpenAI-compatible token usage per session. The dialectical ascent
/// services record after every LLM call; the Sentinel trace export reads and
/// resets the totals when a level completes.
/// </summary>
public interface ILlmUsageCollector
{
    void Add(string sessionId, long inputTokens, long outputTokens, string modelId);
    (long InputTokens, long OutputTokens, string ModelId) GetAndReset(string sessionId);
}

public sealed class LlmUsageCollector : ILlmUsageCollector
{
    private sealed class Entry
    {
        public long Input;
        public long Output;
        public string ModelId = string.Empty;
    }

    private readonly ConcurrentDictionary<string, Entry> _sessions = new(StringComparer.Ordinal);

    public void Add(string sessionId, long inputTokens, long outputTokens, string modelId)
    {
        if (string.IsNullOrEmpty(sessionId)) return;
        var entry = _sessions.GetOrAdd(sessionId, _ => new Entry());
        Interlocked.Add(ref entry.Input, Math.Max(0, inputTokens));
        Interlocked.Add(ref entry.Output, Math.Max(0, outputTokens));
        if (!string.IsNullOrEmpty(modelId)) entry.ModelId = modelId;
    }

    public (long InputTokens, long OutputTokens, string ModelId) GetAndReset(string sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var entry))
            return (0, 0, string.Empty);

        return (Interlocked.Read(ref entry.Input), Interlocked.Read(ref entry.Output), entry.ModelId);
    }
}
