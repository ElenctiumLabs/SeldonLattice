namespace SeldonLattice.Infrastructure.Services;

/// <summary>
/// Ambient session scope for LLM usage tracking. Set during ascent
/// execution so every LLM call (D1, D2, mediator, moderator, fallback)
/// is attributed to the correct Sentinel session regardless of which
/// module or service initiates it.
/// </summary>
public static class LlmUsageScope
{
    private static readonly AsyncLocal<string?> _sessionId = new();

    /// <summary>
    /// Current ambient session ID, or null if no ascent is in progress.
    /// </summary>
    public static string? CurrentSessionId => _sessionId.Value;

    /// <summary>
    /// Enters a scope for the given session. Previous value is restored
    /// on dispose. Supports nested modules (research wraps philosophy + custom modules).
    /// </summary>
    public static IDisposable Begin(string sessionId)
        => new Scope(sessionId);

    /// <summary>
    /// Wraps an async enumerable so the scope is established on every
    /// MoveNext. This is required for async iterators where AsyncLocal
    /// values set inside the iterator body don't persist across yield
    /// boundaries.
    /// </summary>
    public static IAsyncEnumerable<T> Wrap<T>(string sessionId, IAsyncEnumerable<T> source, CancellationToken ct = default)
        => new ScopedAsyncEnumerable<T>(sessionId, source, ct);

    private sealed class Scope : IDisposable
    {
        private readonly string? _previous;

        public Scope(string sessionId)
        {
            _previous = _sessionId.Value;
            _sessionId.Value = sessionId;
        }

        public void Dispose() => _sessionId.Value = _previous;
    }

    private sealed class ScopedAsyncEnumerable<T>(string sessionId, IAsyncEnumerable<T> source, CancellationToken ct) : IAsyncEnumerable<T>
    {
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken token = default)
            => new ScopedEnumerator<T>(sessionId, source.GetAsyncEnumerator(token == default ? ct : token));
    }

    private sealed class ScopedEnumerator<T>(string sessionId, IAsyncEnumerator<T> inner) : IAsyncEnumerator<T>
    {
        private readonly string? _previous = _sessionId.Value;
        private bool _completed;

        public T Current => inner.Current;

        public async ValueTask<bool> MoveNextAsync()
        {
            _sessionId.Value = sessionId;
            try
            {
                var result = await inner.MoveNextAsync();
                if (!result)
                    _completed = true;
                return result;
            }
            catch
            {
                _completed = true;
                throw;
            }
            finally
            {
                if (_completed)
                    _sessionId.Value = _previous;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _sessionId.Value = _previous;
            await inner.DisposeAsync();
        }
    }
}
