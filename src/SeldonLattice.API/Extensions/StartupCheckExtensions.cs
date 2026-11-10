using SeldonLattice.API.Services;
using SeldonLattice.Application.Services;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Options;
using SeldonLattice.Infrastructure.Extensions;

namespace SeldonLattice.API.Extensions;

/// <summary>
/// One-shot startup work that runs after the container is built and the databases are
/// migrated: module registration, Sentinel agent registration, the quality-score/prompt
/// length alignment check, and the optional startup LLM check.
/// </summary>
public static class StartupCheckExtensions
{
    extension(WebApplication app)
    {
        /// <summary>
        /// Registers every resolved dialectics module in the module registry.
        /// </summary>
        public void RegisterSeldonLatticeModules()
        {
            var registry = app.Services.GetRequiredService<IDialecticsModuleRegistry>();
            using var scope = app.Services.CreateScope();
            scope.ServiceProvider.RegisterAllModules(registry);
        }

        /// <summary>
        /// Registers SeldonLattice as a Sentinel agent on startup. Failure is non-fatal - the
        /// registration is retried on the first request.
        /// </summary>
        public async Task EnsureSeldonLatticeAgentRegisteredAsync(SentinelConfiguration sentinelConfig)
        {
            if (!sentinelConfig.Enabled) return;

            try
            {
                using var sentinelScope = app.Services.CreateScope();
                var sentinelService = sentinelScope.ServiceProvider.GetRequiredService<SentinelIntegrationService>();
                await sentinelService.EnsureAgentRegisteredAsync();
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Sentinel agent registration failed; will retry on first request");
            }
        }

        /// <summary>
        /// The quality score's length component is only reachable if the length bracket overlaps the
        /// length the dialecticians are actually asked to produce. When they disagree the component is
        /// unearnable and every score silently caps below 100 - the "stuck at 70%" failure, where the
        /// prompt asked for ~80 words while the bracket started at 400. Checked once at startup so a
        /// future divergence is reported rather than rediscovered from a flat distribution.
        /// </summary>
        public void CheckQualityBracketAlignment()
        {
            var gateOpts = app.Services.GetRequiredService<QualityGateOptions>();
            var diaOpts = app.Services.GetRequiredService<DialecticsOptions>();
            // DialogueQualityGates is scoped, so it must be resolved inside a scope - resolving it
            // from the root provider throws and takes the process down at startup.
            using var bracketScope = app.Services.CreateScope();
            var gates = bracketScope.ServiceProvider.GetRequiredService<DialogueQualityGates>();
            if (gates.IsLengthBracketUnreachable(diaOpts.DialecticianTargetWordCount, out var bracketWarning))
            {
                app.Logger.LogWarning(
                    "QUALITY_SCORE_MISALIGNMENT: {Warning} (QualityGates:OptimalLengthMin={Min}, " +
                    "Dialectics:DialecticianTargetWordCount={Target}, MaxWordCount={Max})",
                    bracketWarning, gateOpts.OptimalLengthMin,
                    diaOpts.DialecticianTargetWordCount, diaOpts.DialecticianMaxWordCount);
            }
        }

        /// <summary>
        /// Probes the configured chat provider at startup so a misconfiguration surfaces in the log
        /// rather than on the first user request. Controlled by DisableStartupLlmCheck.
        /// </summary>
        public async Task RunStartupLlmCheckAsync(bool enabled)
        {
            if (!enabled)
            {
                Console.WriteLine("[DIAG] Startup LLM check disabled by configuration.");
                return;
            }

            try
            {
                var chatService = app.Services.GetRequiredService<IChatService>();
                Console.WriteLine("[DIAG] Testing ChatService...");
                var startupTimeout = app.Services.GetRequiredService<IConfiguration>()
                    .GetValue("HttpClient:StartupCheckTimeoutSeconds", 30);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(startupTimeout));
                var response = await chatService.GetResponseAsync("Hello, are you there?", cts.Token);
                Console.WriteLine($"[DIAG] ChatService Response: {response}");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[DIAG] ChatService startup check timed out.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DIAG] ChatService failed: {ex}");
            }
        }
    }
}
