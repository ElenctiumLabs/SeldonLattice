using Microsoft.Extensions.Options;
using Sentinel.Sdk.Agents;

namespace SeldonLattice.API.Services;

/// <summary>
/// Manages Sentinel agent registration and health checks.
/// Registers SeldonLattice as a Sentinel agent on startup.
/// </summary>
public sealed class SentinelIntegrationService(
    AgentClient agentClient,
    IOptions<SentinelConfiguration> config,
    ILogger<SentinelIntegrationService> logger)
{
    private readonly SentinelConfiguration _config = config.Value;
    private string? _registeredAgentId;

    /// <summary>
    /// Registers SeldonLattice as a Sentinel agent. Idempotent - skips if already registered.
    /// </summary>
    public async Task<string?> EnsureAgentRegisteredAsync(CancellationToken ct = default)
    {
        if (!_config.Enabled)
        {
            logger.LogDebug("Sentinel integration disabled; skipping agent registration");
            return null;
        }

        try
        {
            var existing = await agentClient.GetAgentsAsync(cancellationToken: ct);
            var match = existing.FirstOrDefault(a => a.ExternalId == _config.Agent.ExternalId);

            if (match is not null)
            {
                _registeredAgentId = match.Id;
                logger.LogInformation("Sentinel agent already registered: {AgentId}", match.Id);

                // Ingestion requires an Active agent; activate if still in Draft / Pending
                if (!string.Equals(match.Status.ToString(), "Active", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await agentClient.ActivateAgentAsync(match.Id, "Auto-activated on Seldon Lattice startup", ct);
                        logger.LogInformation("Sentinel agent activated: {AgentId}", match.Id);
                    }
                    catch (Exception actEx)
                    {
                        logger.LogWarning(actEx, "Failed to activate existing Sentinel agent {AgentId}", match.Id);
                    }
                }

                return match.Id;
            }

            var agent = await agentClient.RegisterAgentAsync(new CreateAgentRequest
            {
                ExternalId = _config.Agent.ExternalId,
                Name = _config.Agent.Name,
                Description = _config.Agent.Description,
                Framework = "seldon-lattice",
                ModelProvider = "multi-provider",
                ModelName = "varies",
                Manifest = new CapabilityManifest
                {
                    Tools = ["dialectical-reasoning", "merkle-dag", "prompt-injection-detection"],
                    DataAccess = ["sessions", "nodes", "settings"],
                    Permissions = ["read", "write", "admin"],
                    RateLimits = new Dictionary<string, int>
                    {
                        ["ascend"] = 30,
                        ["ingestion"] = 10
                    }
                }
            }, ct);

            _registeredAgentId = agent.Id;
            logger.LogInformation("Sentinel agent registered: {AgentId} ({ExternalId})", agent.Id, _config.Agent.ExternalId);

            try
            {
                await agentClient.ActivateAgentAsync(agent.Id, "Initial activation on Seldon Lattice registration", ct);
                logger.LogInformation("Sentinel agent activated: {AgentId}", agent.Id);
            }
            catch (Exception actEx)
            {
                logger.LogWarning(actEx, "Failed to activate newly registered Sentinel agent {AgentId}", agent.Id);
            }

            return agent.Id;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // 409 = an agent with this ExternalId already exists (e.g. registered
            // by a previous process). Idempotent success: re-fetch to bind the id.
            try
            {
                var existing = await agentClient.GetAgentsAsync(cancellationToken: ct);
                var match = existing.FirstOrDefault(a => a.ExternalId == _config.Agent.ExternalId);
                if (match is not null)
                {
                    _registeredAgentId = match.Id;
                    logger.LogInformation("Sentinel agent already registered (conflict resolved): {AgentId}", match.Id);

                    if (string.Equals(match.Status.ToString(), "Active", StringComparison.OrdinalIgnoreCase))
                        return match.Id;
                    try
                    {
                        await agentClient.ActivateAgentAsync(match.Id, "Auto-activated on conflict resolution", ct);
                        logger.LogInformation("Sentinel agent activated: {AgentId}", match.Id);
                    }
                    catch (Exception actEx)
                    {
                        logger.LogWarning(actEx, "Failed to activate Sentinel agent {AgentId} after conflict resolution", match.Id);
                    }

                    return match.Id;
                }
            }
            catch (Exception inner)
            {
                logger.LogWarning(inner, "409 on registration but re-fetch failed");
            }

            logger.LogWarning("Sentinel agent registration returned 409 but no matching agent was found; will retry later");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to register Sentinel agent; will retry on next request");
            return null;
        }
    }

    /// <summary>
    /// Returns the cached agent ID, or attempts registration if not yet done.
    /// </summary>
    public async Task<string?> GetAgentIdAsync(CancellationToken ct = default)
    {
        return _registeredAgentId ?? await EnsureAgentRegisteredAsync(ct);
    }
}
