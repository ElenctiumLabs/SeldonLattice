using Microsoft.EntityFrameworkCore;
using SeldonLattice.Domain.Models.Settings;

namespace SeldonLattice.Infrastructure.Settings;

/// <summary>
/// EF Core DbContext for the settings SQLite database.
/// The database file is created in the application's data directory
/// (controlled via the "DataDir" configuration key).
/// </summary>
public sealed class SettingsDbContext(DbContextOptions<SettingsDbContext> options) : DbContext(options)
{
    public DbSet<FrameworkSettings> FrameworkSettings => Set<FrameworkSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<FrameworkSettings>();

        entity.HasKey(e => e.Id);

        // Enforce one settings row per framework - the repository uses upsert
        // semantics so this constraint should never be violated at runtime.
        entity.HasIndex(e => e.Framework).IsUnique();

        entity.Property(e => e.Framework).HasMaxLength(64).IsRequired();
        entity.Property(e => e.UserName).HasMaxLength(128);
        entity.Property(e => e.Provider).HasMaxLength(128);
        entity.Property(e => e.Model).HasMaxLength(256);
        entity.Property(e => e.ApiEndpoint).HasMaxLength(512);
        entity.Property(e => e.LayoutMode).HasMaxLength(32);
        entity.Property(e => e.FontSize).HasMaxLength(16);
        entity.Property(e => e.ExportFormat).HasMaxLength(16);
        entity.Property(e => e.SessionPrefix).HasMaxLength(128);
        entity.Property(e => e.NotificationEmail).HasMaxLength(320);
        entity.Property(e => e.WebhookUrl).HasMaxLength(1024);

        // Store API key and auth token - kept short for security
        entity.Property(e => e.ApiKey).HasMaxLength(512);
        entity.Property(e => e.AuthToken).HasMaxLength(1024);

        // Agents JSON blob - no column length limit
        entity.Property(e => e.AgentsJson).HasColumnType("TEXT");

        // Engine config JSON blobs - nullable, no length limit
        entity.Property(e => e.QualityGatesJson).HasColumnType("TEXT");
        entity.Property(e => e.DialecticsJson).HasColumnType("TEXT");
    }
}
