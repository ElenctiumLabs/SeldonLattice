using Microsoft.EntityFrameworkCore;
using SeldonLattice.API.Services;
using SeldonLattice.API.Services.Outbox;
using SeldonLattice.Application.Security;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Infrastructure.Services;
using Sentinel.Sdk.Extensions;
using Serilog;

namespace SeldonLattice.API.Extensions;

/// <summary>
/// Sentinel integration: the SDK client, the agent/trace services, the security scanner
/// adapter, and the at-least-once trace-export outbox (with its flush worker) used while
/// Sentinel is unreachable.
/// </summary>
public static class SentinelExtensions
{
    /// <summary>
    /// Registers Sentinel when enabled. Returns the parsed configuration so the caller can
    /// skip the agent-registration and outbox startup work when integration is off.
    /// </summary>
    public static SentinelConfiguration AddSeldonLatticeSentinel(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SentinelConfiguration>(configuration.GetSection("Sentinel"));
        services.AddSingleton<PromptInjectionSecurityGuard>();
        services.AddSingleton<ILlmUsageCollector, LlmUsageCollector>();

        var sentinelConfig = configuration.GetSection("Sentinel").Get<SentinelConfiguration>()
                              ?? new SentinelConfiguration();

        if (sentinelConfig.Enabled && !string.IsNullOrEmpty(sentinelConfig.ApiKey))
        {
            services.AddSentinelSdkWithApiKey(sentinelConfig.ApiKey, sentinelConfig.BaseUrl);
            services.AddSingleton<SentinelIntegrationService>();
            services.AddSingleton<SessionTraceService>();
            services.AddSingleton<ISentinelSecurityScanner, SentinelSecurityAdapter>();

            // -- Trace-export outbox (at-least-once delivery while Sentinel is unreachable) --
            var sentinelDataDir = Path.GetFullPath(configuration.GetValue("DataDir", "data"));
            Directory.CreateDirectory(sentinelDataDir);
            var outboxDbPath = Path.Combine(sentinelDataDir, "sentinel-outbox.db");
            services.AddDbContextFactory<TraceOutboxDbContext>(options =>
                options.UseSqlite($"Data Source={outboxDbPath}"));
            services.AddSingleton<ISentinelTraceOutbox, SentinelTraceOutbox>();
            services.AddHostedService<SentinelOutboxFlushWorker>();

            Log.Information("Sentinel integration enabled - connecting to {BaseUrl}", sentinelConfig.BaseUrl);
        }
        else
        {
            Log.Information("Sentinel integration disabled - using local security guard only");
        }

        return sentinelConfig;
    }
}
