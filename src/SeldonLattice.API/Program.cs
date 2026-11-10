using SeldonLattice.API.Endpoints;
using SeldonLattice.API.Extensions;

namespace SeldonLattice.API;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Logging + observability (Serilog, OTel traces/meters, metrics, non-generic ILogger).
        builder.Host.AddSeldonLatticeLogging();
        builder.Services.AddSeldonLatticeObservability(builder.Configuration);

        // HTTP surface: CORS, API-key auth, authorization policies, JSON, Swagger.
        builder.Services.AddSeldonLatticeHttpServices(builder.Configuration);

        // Merkle store, ML-DSA-65 signing, sigil generator, startup-time health probe.
        builder.Services.AddSeldonLatticeMerkleStore(builder.Configuration);

        // Semantic Kernel: chat + embedding services for the configured LLM provider.
        builder.Services.AddSeldonLatticeKernel(builder.Configuration);

        // Dialectical reasoning engine: options, registry, quality gates, all modules.
        builder.Services.AddSeldonLatticeDialectics(builder.Configuration);

        // Sentinel integration (agent registration + trace-export outbox when enabled).
        var sentinelConfig = builder.Services.AddSeldonLatticeSentinel(builder.Configuration);

        // External data ingestion & falsification module.
        builder.Services.AddSeldonLatticeIngestion(builder.Configuration);

        // SQLite persistence (settings + session metadata).
        builder.Services.AddSeldonLatticePersistence(builder.Configuration);

        var app = builder.Build();

        // Create the SQLite schema on first run and apply the additive column migrations.
        await app.EnsureSeldonLatticeSchemaAsync(sentinelConfig);

        // Seed default framework settings & engine config into DB.
        await app.SeedDefaultFrameworkSettingsAsync();

        // Promote persisted provider/engine settings into the runtime before the Kernel
        // and modules are resolved.
        await app.ApplyPersistedSettingsAsync();

        // Start both local llama.cpp processes for Development runs. Chat startup returns
        // after a short readiness grace period because loading the 30B model can take longer;
        // the launcher tracks and reports it as starting until the port is ready.
        if (app.Environment.IsDevelopment() &&
            app.Configuration["LlmProvider"] == "LlamaCpp")
        {
            var serverLauncher = app.Services.GetRequiredService<Services.ServerLaunchService>();
            foreach (var server in new[] { "chat", "embeddings" })
            {
                var launch = await serverLauncher.LaunchServerAsync(server);
                if (launch.Errors.Count > 0)
                {
                    app.Logger.LogWarning("Local {Server} server did not start: {Errors}",
                        server, string.Join("; ", launch.Errors));
                }
                if (launch.Starting.Count > 0)
                {
                    app.Logger.LogInformation("Local {Server} server is still loading", server);
                }
            }
        }

        // One-shot startup work: modules, Sentinel agent, quality bracket, LLM probe.
        app.RegisterSeldonLatticeModules();
        await app.EnsureSeldonLatticeAgentRegisteredAsync(sentinelConfig);
        app.CheckQualityBracketAlignment();
        await app.RunStartupLlmCheckAsync(
            !builder.Configuration.GetValue("DisableStartupLlmCheck", false));

        // HTTP pipeline and route groups.
        app.ConfigureSeldonLatticePipeline();
        app.MapSeldonLatticeEndpoints();

// Fail fast when the host is not configured with a production stream-token secret.
        app.EnsureStreamTokenSecretConfigured();

        await app.RunAsync();
    }
}
