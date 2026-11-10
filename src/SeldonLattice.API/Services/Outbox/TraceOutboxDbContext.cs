using Microsoft.EntityFrameworkCore;

namespace SeldonLattice.API.Services.Outbox;

/// <summary>
/// EF Core DbContext for the trace-export outbox (dedicated SQLite database file in
/// the application data directory). Deliberately separate from sessions/settings
/// databases so outbox growth and retention never affects reasoning metadata.
/// </summary>
public sealed class TraceOutboxDbContext(DbContextOptions<TraceOutboxDbContext> options) : DbContext(options)
{
    public DbSet<PendingTraceExport> PendingTraces => Set<PendingTraceExport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PendingTraceExport>();

        entity.Property(e => e.TraceJson).HasColumnType("TEXT").IsRequired();
        entity.Property(e => e.SessionId).HasMaxLength(64);
        entity.Property(e => e.LastError).HasColumnType("TEXT");

        // Flush path: oldest due items first.
        entity.HasIndex(e => new { e.NextAttemptAtUtc, e.CreatedAtUtc })
            .HasDatabaseName("IX_PendingTraces_NextAttempt_CreatedAt");
    }
}
