using SeldonLattice.Domain.Models.Settings;

namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Persistence contract for per-framework settings stored in SQLite.
/// </summary>
public interface ISettingsRepository
{
    /// <summary>
    /// Returns the settings for the given framework, or null if none have been saved yet.
    /// </summary>
    Task<FrameworkSettings?> GetAsync(string framework, CancellationToken ct = default);

    /// <summary>
    /// Creates or updates the settings for the given framework.
    /// Sets <see cref="FrameworkSettings.IsConfigured"/> = true and updates
    /// <see cref="FrameworkSettings.UpdatedAt"/> automatically.
    /// </summary>
    Task<FrameworkSettings> UpsertAsync(FrameworkSettings settings, CancellationToken ct = default);

    /// <summary>
    /// Returns true if the framework has been explicitly configured by the user at least once.
    /// </summary>
    Task<bool> IsConfiguredAsync(string framework, CancellationToken ct = default);
}
