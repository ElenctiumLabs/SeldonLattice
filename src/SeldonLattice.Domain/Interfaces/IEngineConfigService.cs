using SeldonLattice.Domain.Options;

namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Resolves runtime engine configuration (QualityGates and Dialectics) for a given framework.
/// Values are read from the SQLite settings store (DB-first) with memory caching.
/// This replaces the static IOptions&lt;T&gt; pattern so that settings changes take effect
/// without an application restart.
/// </summary>
public interface IEngineConfigService
{
    /// <summary>
    /// Returns the effective QualityGateOptions for the given framework.
    /// DB row wins if present; otherwise returns the seeded defaults.
    /// </summary>
    Task<QualityGateOptions> GetQualityGateOptionsAsync(string framework, CancellationToken ct = default);

    /// <summary>
    /// Returns the effective DialecticsOptions for the given framework.
    /// DB row wins if present; otherwise returns the seeded defaults.
    /// Stored engine config JSON is deserialised back into the base options type.
    /// </summary>
    Task<DialecticsOptions> GetDialecticsOptionsAsync(string framework, CancellationToken ct = default);

    /// <summary>
    /// Invalidates the cache for the given framework so the next call re-reads from DB.
    /// Called automatically after a settings save.
    /// </summary>
    void InvalidateCache(string framework);

    /// <summary>
    /// Copies the persisted options for the framework into the live singleton
    /// <see cref="QualityGateOptions"/> and <see cref="DialecticsOptions"/> instances held
    /// by the agents, so a settings save takes effect without a restart.
    /// </summary>
    Task ApplyRuntimeOptionsAsync(string framework, CancellationToken ct = default);
}
