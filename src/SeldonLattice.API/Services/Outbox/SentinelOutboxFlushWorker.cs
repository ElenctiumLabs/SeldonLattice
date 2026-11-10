using Microsoft.Extensions.Options;

namespace SeldonLattice.API.Services.Outbox;

/// <summary>
/// Background flusher for the Sentinel trace outbox. Registered only when the
/// Sentinel integration is enabled; exits immediately otherwise.
/// </summary>
public sealed class SentinelOutboxFlushWorker(
    ISentinelTraceOutbox outbox,
    IOptions<SentinelConfiguration> config,
    ILogger<SentinelOutboxFlushWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.Value.Enabled || !config.Value.Tracing.Enabled)
            return;

        logger.LogInformation("Sentinel trace outbox flusher started");
        var firstRun = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Deliver anything queued before shutdown on startup.
                var delivered = await outbox.FlushDueAsync(stoppingToken);
                if (delivered > 0)
                {
                    logger.LogInformation("Flushed {Delivered} queued trace(s) to Sentinel", delivered);
                }

                if (firstRun && await outbox.CountPendingAsync(stoppingToken) > 0)
                {
                    logger.LogWarning("Sentinel unreachable; queued traces will retry with exponential backoff");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Sentinel outbox flush cycle failed; retrying next interval");
            }

            firstRun = false;
            try
            {
                await Task.Delay(IdleInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("Sentinel trace outbox flusher stopped");
    }
}
