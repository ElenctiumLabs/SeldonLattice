using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Infrastructure.Sessions;

namespace SeldonLattice.Application.Tests;

/// <summary>
/// Registers the SQLite-backed <see cref="SessionService"/> (the real implementation
/// used in production) into a test DI container, and ensures the schema exists.
/// Convenience test-session helper retained under the Application test project.
/// </summary>
internal static class TestSessionSupport
{
    public static void AddTestSessionService(IServiceCollection services, string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        services.AddDbContextFactory<SessionsDbContext>(options =>
            options.UseSqlite($"Data Source={Path.Combine(dataDir, "sessions.db")}"));
        services.AddScoped<ISessionService, SessionService>();
    }

    public static void EnsureSessionsCreated(IServiceProvider provider)
    {
        var db = provider.GetRequiredService<IDbContextFactory<SessionsDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
        db.Dispose();
    }

    public const string OwnerId = "test-user";
}