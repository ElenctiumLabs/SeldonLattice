using Microsoft.EntityFrameworkCore;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Settings;

namespace SeldonLattice.Infrastructure.Settings;

/// <summary>
/// EF Core–backed implementation of <see cref="ISettingsRepository"/>.
/// Uses SQLite as the backing store via <see cref="SettingsDbContext"/>.
/// </summary>
public sealed class SettingsRepository(SettingsDbContext db) : ISettingsRepository
{
    /// <inheritdoc />
    public async Task<FrameworkSettings?> GetAsync(string framework, CancellationToken ct = default)
    {
        return await db.FrameworkSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Framework == framework.ToLowerInvariant(), ct);
    }

    /// <inheritdoc />
    public async Task<FrameworkSettings> UpsertAsync(FrameworkSettings settings, CancellationToken ct = default)
    {
        var key = settings.Framework.ToLowerInvariant();
        var existing = await db.FrameworkSettings
            .FirstOrDefaultAsync(s => s.Framework == key, ct);

        settings.Framework = key;
        settings.UpdatedAt = DateTime.UtcNow;

        if (existing is null)
        {
            settings.CreatedAt = DateTime.UtcNow;
            db.FrameworkSettings.Add(settings);
        }
        else
        {
            // Update all mutable fields while preserving the PK and CreatedAt.
            // IsConfigured is caller-controlled: the seeder must be able to write a row
            // without marking it as a deliberate user save.
            existing.IsConfigured = settings.IsConfigured;
            existing.UpdatedAt = settings.UpdatedAt;
            existing.UserName = settings.UserName;
            existing.Provider = settings.Provider;
            existing.Model = settings.Model;
            existing.ApiEndpoint = settings.ApiEndpoint;
            existing.ApiKey = settings.ApiKey;
            existing.AuthToken = settings.AuthToken;
            existing.RateLimit = settings.RateLimit;
            existing.LayoutMode = settings.LayoutMode;
            existing.MaxDepth = settings.MaxDepth;
            existing.QualityThreshold = settings.QualityThreshold;
            existing.EnableStreamingSSE = settings.EnableStreamingSSE;
            existing.UseStaticQuestions = settings.UseStaticQuestions;
            existing.EnableThinking = settings.EnableThinking;
            existing.PersistReasoningTrace = settings.PersistReasoningTrace;
            existing.FontSize = settings.FontSize;
            existing.EnableAnimations = settings.EnableAnimations;
            existing.SessionMaxDepth = settings.SessionMaxDepth;
            existing.AutoArchiveDays = settings.AutoArchiveDays;
            existing.SessionPrefix = settings.SessionPrefix;
            existing.ExportFormat = settings.ExportFormat;
            existing.IncludeMerkleProofs = settings.IncludeMerkleProofs;
            existing.IncludeMetrics = settings.IncludeMetrics;
            existing.ComplianceReport = settings.ComplianceReport;
            existing.NotifyOnSynthesis = settings.NotifyOnSynthesis;
            existing.NotifyOnGateRejection = settings.NotifyOnGateRejection;
            existing.NotifyOnSessionStall = settings.NotifyOnSessionStall;
            existing.NotifyOnMaxDepth = settings.NotifyOnMaxDepth;
            existing.NotificationEmail = settings.NotificationEmail;
            existing.WebhookUrl = settings.WebhookUrl;
            existing.AgentsJson = settings.AgentsJson;
            existing.QualityGatesJson = settings.QualityGatesJson;
            existing.DialecticsJson = settings.DialecticsJson;
            settings = existing;
        }

        await db.SaveChangesAsync(ct);
        return settings;
    }

    /// <inheritdoc />
    public async Task<bool> IsConfiguredAsync(string framework, CancellationToken ct = default)
    {
        return await db.FrameworkSettings
            .AsNoTracking()
            .AnyAsync(s => s.Framework == framework.ToLowerInvariant() && s.IsConfigured, ct);
    }
}
