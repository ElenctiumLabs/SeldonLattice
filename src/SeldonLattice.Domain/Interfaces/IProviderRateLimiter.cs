namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Enforces per-provider rate limits for outbound LLM calls.
/// The provider is determined at construction time from configuration.
/// Tracks requests per minute using a sliding window and blocks
/// when the configured RPM limit is reached.
/// </summary>
public interface IProviderRateLimiter
{
    /// <summary>
    /// Blocks until a request slot is available, respecting the configured RPM limit.
    /// Returns immediately for providers with no configured limit (e.g. local models).
    /// </summary>
    Task WaitForSlotAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns true if a request slot is currently available
    /// (i.e. the RPM limit has not been exceeded in the current window).
    /// Returns true for providers with no configured limit.
    /// </summary>
    bool IsAvailable();

    /// <summary>
    /// Records that a request was made. Called after a successful LLM call
    /// to keep the window accurate.
    /// </summary>
    void RecordRequest();
}
