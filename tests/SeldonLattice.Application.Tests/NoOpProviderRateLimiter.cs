using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.Application.Tests;

/// <summary>
/// No-op rate limiter for unit tests. All requests pass through immediately.
/// </summary>
public sealed class NoOpProviderRateLimiter : IProviderRateLimiter
{
    public Task WaitForSlotAsync(CancellationToken ct = default) => Task.CompletedTask;
    public bool IsAvailable() => true;
    public void RecordRequest() { }
}
