using SeldonLattice.API.Middleware;
using Sentinel.Telemetry;
using Serilog;
using Serilog.Events;

namespace SeldonLattice.API.Extensions;

/// <summary>
/// Serilog bootstrap and the shared observability services: OTel traces/meters, the
/// metrics singleton, and the non-generic ILogger that minimal API handlers resolve via
/// <c>[FromServices] ILogger</c>.
/// </summary>
public static class LoggingExtensions
{
    public static void AddSeldonLatticeLogging(this IHostBuilder host)
    {
        // Add Serilog via shared Plainsentinel.Telemetry bootstrap
        // (console + Seq when Seq:ServerUrl configured + service.name enrichment).
        // Console output template deliberately omits {SourceContext} so the debug log
        // shows only the log line, not the fully-qualified type/file name.
        host.UseSerilog((context, services, configuration) =>
        {
            // Console output template deliberately omits {SourceContext} so the debug log
            // shows only the log line, not the fully-qualified type/file name.
            configuration
                .Enrich.WithProperty("service.name", "SeldonLattice")
                .Enrich.FromLogContext()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:l}{NewLine}{Exception}",
                    theme: Serilog.Sinks.SystemConsole.Themes.SystemConsoleTheme.Literate);

            // Keep full local-inference process output available during debugging without
            // raising the application's global log level or enabling noisy framework logs.
            if (context.HostingEnvironment.IsDevelopment())
                configuration.MinimumLevel.Override(
                    "SeldonLattice.API.Services.ServerLaunchService", LogEventLevel.Debug);

            var seqUrl = context.Configuration["Seq:ServerUrl"];
            if (!string.IsNullOrEmpty(seqUrl))
                configuration.WriteTo.Seq(seqUrl);

            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services);
        });
    }

    public static void AddSeldonLatticeObservability(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Shared OTel traces/meters (inactive without OpenTelemetry:OtlpEndpoint).
        services.AddSentinelTelemetry(configuration, "SeldonLattice");

        // Add observability metrics
        services.AddSingleton<SeldonLatticeMetrics>();

        // Register the non-generic ILogger so minimal API handlers using
        // [FromServices] ILogger logger resolve correctly.
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger>(sp =>
            sp.GetRequiredService<ILoggerFactory>().CreateLogger("SeldonLattice.API"));

        // Add Activity Source for distributed tracing (uses built-in System.Diagnostics)

        // Add services to the container.
        services.AddEndpointsApiExplorer();
    }
}
