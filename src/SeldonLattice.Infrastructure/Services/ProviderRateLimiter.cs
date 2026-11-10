using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Options;

namespace SeldonLattice.Infrastructure.Services;

/// <summary>
/// Thread-safe sliding-window rate limiter for outbound LLM calls.
/// Resolves the active provider name from IConfiguration at construction,
/// then enforces per-provider RPM limits. Uses a cleanup interval to
/// purge stale timestamps and avoid unbounded memory growth.
/// </summary>
public sealed class ProviderRateLimiter(
    IConfiguration config,
    IOptions<ProviderRateLimitOptions> options,
    ILogger<ProviderRateLimiter> logger)
    : IProviderRateLimiter
{
    private readonly string _provider = config["LlmProvider"] ?? "Ollama";
    private readonly ProviderRateLimitOptions _options = options.Value;

    // Sliding window of request timestamps for the active provider
    private readonly List<DateTimeOffset> _window = [];
    private readonly object _lock = new();

    private static readonly TimeSpan WindowSize = TimeSpan.FromMinutes(1);

    public bool IsAvailable()
    {
        if (!_options.Providers.TryGetValue(_provider, out var limit))
            return true; // No configured limit = always available

        var cutoff = DateTimeOffset.UtcNow - WindowSize;
        lock (_lock)
        {
            PruneOldEntries(_window, cutoff);
            return _window.Count < limit.RequestsPerMinute;
        }
    }

    public async Task WaitForSlotAsync(CancellationToken ct = default)
    {
        if (!_options.Providers.TryGetValue(_provider, out var limit))
            return; // No configured limit = no wait needed

        var maxWait = TimeSpan.FromSeconds(_options.MaxWaitSeconds);
        var deadline = DateTimeOffset.UtcNow + maxWait;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (DateTimeOffset.UtcNow >= deadline)
            {
                logger.LogWarning(
                    "Rate limit wait timed out for provider {Provider} after {MaxWait}s",
                    _provider, _options.MaxWaitSeconds);
                throw new TimeoutException(
                    $"Rate limit wait timed out for provider '{_provider}' after {_options.MaxWaitSeconds}s");
            }

            var now = DateTimeOffset.UtcNow;
            var cutoff = now - WindowSize;
            TimeSpan waitTime;

            lock (_lock)
            {
                PruneOldEntries(_window, cutoff);

                if (_window.Count < limit.RequestsPerMinute)
                {
                    _window.Add(now);
                    logger.LogDebug(
                        "Rate limit slot granted for {Provider} ({Count}/{Limit} RPM)",
                        _provider, _window.Count, limit.RequestsPerMinute);
                    return;
                }

                var oldestInWindow = _window[0];
                var waitUntil = oldestInWindow + WindowSize;
                waitTime = waitUntil - now;

                logger.LogDebug(
                    "Rate limit reached for {Provider} ({Count}/{Limit} RPM), waiting {WaitMs}ms",
                    _provider, _window.Count, limit.RequestsPerMinute, (int)waitTime.TotalMilliseconds);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(500, (int)waitTime.TotalMilliseconds)), ct);
        }
    }

    public void RecordRequest()
    {
        lock (_lock)
        {
            _window.Add(DateTimeOffset.UtcNow);
        }
    }

    private static void PruneOldEntries(List<DateTimeOffset> timestamps, DateTimeOffset cutoff)
    {
        var i = 0;
        while (i < timestamps.Count && timestamps[i] < cutoff)
            i++;
        if (i > 0)
            timestamps.RemoveRange(0, i);
    }
}
