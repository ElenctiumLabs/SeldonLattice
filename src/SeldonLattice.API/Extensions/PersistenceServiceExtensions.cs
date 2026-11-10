using Microsoft.EntityFrameworkCore;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Infrastructure.Sessions;
using SeldonLattice.Infrastructure.Settings;

namespace SeldonLattice.API.Extensions;

/// <summary>
/// SQLite persistence registrations. Two databases, both under the application data
/// directory controlled by the "DataDir" configuration key:
///   - settings.db  : per-framework configuration (FrameworkSettings)
///   - sessions.db  : session metadata (the single source of truth for sessions)
/// </summary>
public static class PersistenceServiceExtensions
{
    public static void AddSeldonLatticePersistence(this IServiceCollection services,
        IConfiguration configuration)
    {
        var settingsDir = Path.GetFullPath(configuration["DataDir"] ?? "data");
        Directory.CreateDirectory(settingsDir);

        // -- Settings SQLite persistence ------------------------------------------
        var settingsDbPath = Path.Combine(settingsDir, "settings.db");
        services.AddDbContext<SettingsDbContext>(options =>
            options.UseSqlite($"Data Source={settingsDbPath}"));
        services.AddScoped<ISettingsRepository, SettingsRepository>();

        // -- Session SQLite persistence (single source of truth for session metadata) --
        var sessionsDbPath = Path.Combine(settingsDir, "sessions.db");
        // Registered as a factory so the SessionService can create short-lived contexts per
        // operation. The ascent services resolve session metadata while SSE streaming, which
        // outlives the HTTP request scope - a request-scoped DbContext would be disposed by
        // the time the streamed ascent reads the session.
        services.AddDbContextFactory<SessionsDbContext>(options =>
            options.UseSqlite($"Data Source={sessionsDbPath}"));
        services.AddScoped<ISessionService, SessionService>();
    }
}
