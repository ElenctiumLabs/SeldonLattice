using Microsoft.EntityFrameworkCore;
using SeldonLattice.API.Services;
using SeldonLattice.API.Services.Outbox;
using SeldonLattice.Infrastructure.Sessions;
using SeldonLattice.Infrastructure.Settings;

namespace SeldonLattice.API.Extensions;

/// <summary>
/// Creates the SQLite schema on first run and applies the additive column migrations the
/// settings table has accumulated. Runs once at startup, before the app serves traffic, so a
/// partially-migrated database never serves a request.
/// </summary>
public static class PersistenceStartupExtensions
{
    public static async Task EnsureSeldonLatticeSchemaAsync(
        this WebApplication app,
        SentinelConfiguration? sentinelConfig = null)
    {
        using var scope = app.Services.CreateScope();

        var settingsDb = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();
        await settingsDb.Database.EnsureCreatedAsync();

        // Sessions SQLite database - single source of truth for session metadata.
        var sessionsDb = await scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<SessionsDbContext>>()
            .CreateDbContextAsync();
        await sessionsDb.Database.EnsureCreatedAsync();
        await sessionsDb.DisposeAsync();

        // Sentinel trace-export outbox database (only when integration enabled).
        if (sentinelConfig?.Enabled == true)
        {
            try
            {
                var outboxDb = await scope.ServiceProvider
                    .GetRequiredService<IDbContextFactory<TraceOutboxDbContext>>()
                    .CreateDbContextAsync();
                await outboxDb.Database.EnsureCreatedAsync();
                await outboxDb.DisposeAsync();
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Failed to initialize the Sentinel trace outbox database");
            }
        }

        await MigrateFrameworkSettingsColumnsAsync(app, settingsDb);
    }

    /// <summary>
    /// The settings table is extended in place rather than recreated, so existing user
    /// configuration survives a deployment. Each column is added only when absent. The retired
    /// LoRA module-adapter column is dropped when present, since the additive migrations no
    /// longer add it and the mapped property is gone.
    /// </summary>
    private static async Task MigrateFrameworkSettingsColumnsAsync(
        WebApplication app,
        SettingsDbContext settingsDb)
    {
        // Check if new columns exist before running ALTER TABLE (avoids throwing errors if columns already exist)
        try
        {
            var conn = settingsDb.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA table_info('FrameworkSettings');";
            var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    existingColumns.Add(reader.GetString(1)); // column 1 is name
                }
            }

            foreach (var (name, ddl) in FrameworkSettingsColumnMigrations)
            {
                if (existingColumns.Contains(name)) continue;

                await using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = ddl;
                await alterCmd.ExecuteNonQueryAsync();
            }

            // One-shot cleanup for databases created before the LoRA removal: the additive
            // migrations above never add LoraModuleAdapter to a new database, and the mapped
            // property is gone, so any legacy column is now dead weight. Drop it when present.
            if (existingColumns.Contains("LoraModuleAdapter"))
            {
                await using var dropCmd = conn.CreateCommand();
                dropCmd.CommandText = "ALTER TABLE FrameworkSettings DROP COLUMN LoraModuleAdapter;";
                await dropCmd.ExecuteNonQueryAsync();
                app.Logger.LogInformation("FrameworkSettings migration: dropped retired LoraModuleAdapter column.");
            }
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Schema migration check on FrameworkSettings table completed with notice");
        }
    }

    private static readonly (string Name, string Ddl)[] FrameworkSettingsColumnMigrations =
    [
        ("QualityGatesJson", "ALTER TABLE FrameworkSettings ADD COLUMN QualityGatesJson TEXT;"),
        ("DialecticsJson", "ALTER TABLE FrameworkSettings ADD COLUMN DialecticsJson TEXT;"),
        ("UserName", "ALTER TABLE FrameworkSettings ADD COLUMN UserName TEXT NOT NULL DEFAULT '';"),
        ("EnableThinking", "ALTER TABLE FrameworkSettings ADD COLUMN EnableThinking INTEGER NOT NULL DEFAULT 0;"),
        ("PersistReasoningTrace",
            "ALTER TABLE FrameworkSettings ADD COLUMN PersistReasoningTrace INTEGER NOT NULL DEFAULT 0;"),
    ];
}
