using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeldonLattice.Domain.Configuration;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Infrastructure.Modules;
using SeldonLattice.Infrastructure.Services;

namespace SeldonLattice.API.Controllers;

/// <summary>
/// API endpoints for creating and managing custom dialectics modules.
/// </summary>
[ApiController]
[Route("api/modules")]
public class ModuleController(
    IDialecticsModuleRegistry registry,
    IServiceProvider serviceProvider,
    ILogger<ModuleController> logger)
    : ControllerBase
{
    /// <summary>
    /// Creates a custom dialectics module from a module definition.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public IResult CreateModule(
        [FromBody] ModuleConfiguration configuration,
        [FromServices] DialecticalAscentServiceBase ascentService)
    {
        // Validate the module configuration
        if (string.IsNullOrWhiteSpace(configuration.Key))
        {
            logger.LogWarning("Module creation failed: missing Key");
            return Results.BadRequest(new { error = "Module Key is required" });
        }

        if (string.IsNullOrWhiteSpace(configuration.Name))
        {
            logger.LogWarning("Module creation failed: missing Name for module {Key}", configuration.Key);
            return Results.BadRequest(new { error = "Module Name is required" });
        }

        if (configuration.Agents.Length < 2)
        {
            logger.LogWarning("Module creation failed: insufficient agents for module {Key}", configuration.Key);
            return Results.BadRequest(new { error = "At least 2 agents are required" });
        }

        // Validate agent names are unique
        var agentNames = configuration.Agents.Select(a => a.Name).Where(_ => true).ToArray();
        if (agentNames.Length != agentNames.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            logger.LogWarning("Module creation failed: duplicate agent names for module {Key}", configuration.Key);
            return Results.BadRequest(new { error = "Agent names must be unique" });
        }

        // Check if key already exists
        if (registry.GetAvailableModules().Any(k => k.Equals(configuration.Key, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogWarning("Module creation failed: module {Key} already exists", configuration.Key);
            return Results.Conflict(new { error = $"A module with key '{configuration.Key}' already exists" });
        }

        try
        {
            var module = new DynamicDialecticsModule(serviceProvider, ascentService, configuration);
            registry.RegisterModule(configuration.Key, module);
            registry.RegisterModuleDefinition(configuration.Key, configuration);

            logger.LogInformation("Created custom module: {Key} ({Name}) with {AgentCount} agents",
                configuration.Key, configuration.Name, configuration.Agents.Length);

            return Results.Created($"/api/modules/{configuration.Key}", new
            {
                key = configuration.Key,
                name = configuration.Name,
                agentCount = configuration.Agents.Length,
                qualityGateCount = configuration.QualityGates?.Length ?? 0
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create custom module {Key}", configuration.Key);
            return Results.Problem($"Failed to create module: {ex.Message}", statusCode: 500);
        }
    }

    /// <summary>
    /// Lists all registered dialectics modules and their definitions.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "ReaderOnly")]
    public IResult ListModules()
    {
        var moduleKeys = registry.GetAvailableModules().ToArray();
        var definitions = registry.GetAllModuleDefinitions().ToArray();

        var result = moduleKeys.Select(key =>
        {
            var module = registry.GetModule(key);
            var def = definitions.FirstOrDefault(d => d.Key == key);
            return new
            {
                key,
                name = module.DisplayName,
                description = module.Description,
                isCustom = def != null,
                agents = def?.Agents.Select(a => new { a.Name, a.Framework, a.Description }).ToArray(),
                qualityGates = def?.QualityGates,
                mediatorConfig = def?.MediatorConfig,
                moderatorConfig = def?.ModeratorConfig
            };
        });

        return Results.Ok(result);
    }

    /// <summary>
    /// Returns a specific module's configuration by key.
    /// </summary>
    [HttpGet("{key}")]
    [Authorize(Policy = "ReaderOnly")]
    public IResult GetModule(string key)
    {
        if (string.IsNullOrEmpty(key))
            return Results.BadRequest(new { error = "Key is required" });

        try
        {
            var module = registry.GetModule(key);
            var def = registry.GetModuleDefinition(key);

            return Results.Ok(new
            {
                key,
                name = module.DisplayName,
                description = module.Description,
                isCustom = def != null,
                agents = def?.Agents.Select(a => new { a.Name, a.Framework, a.Description, a.Style, a.Expertise }).ToArray(),
                qualityGates = def?.QualityGates,
                mediatorConfig = def?.MediatorConfig,
                moderatorConfig = def?.ModeratorConfig
            });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = $"Module '{key}' not found" });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to retrieve module: {ex.Message}", statusCode: 500);
        }
    }
}
