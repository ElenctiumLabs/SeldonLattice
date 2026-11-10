using Microsoft.EntityFrameworkCore;
using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Infrastructure.Sessions;

/// <summary>
/// EF Core DbContext for the sessions SQLite database (single source of truth for
/// session metadata). The database file is created in the application's data directory
/// (controlled via the "DataDir" configuration key).
/// </summary>
public sealed class SessionsDbContext(DbContextOptions<SessionsDbContext> options) : DbContext(options)
{
    public DbSet<Session> Sessions => Set<Session>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Session>();

        entity.HasKey(e => e.SessionId);
        entity.Property(e => e.SessionId).HasMaxLength(64);

        entity.Property(e => e.Framework).HasMaxLength(64).IsRequired();
        entity.Property(e => e.RootQuestion).HasColumnType("TEXT").IsRequired();
        entity.Property(e => e.OwnerId).HasMaxLength(256).IsRequired();

        entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);

        // Ownership + framework scoping is the primary query path for the UI
        // (list sessions for the current user, grouped by framework).
        entity.HasIndex(e => new { e.OwnerId, e.Framework, e.CreatedAt })
            .HasDatabaseName("IX_Sessions_Owner_Framework_CreatedAt");
    }
}