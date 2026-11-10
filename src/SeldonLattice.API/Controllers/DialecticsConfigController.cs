using Microsoft.AspNetCore.Mvc;
using SeldonLattice.API.Models;
using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.API.Controllers;

/// <summary>
/// Runtime dialectics configuration for a framework: available modules, ascent limits,
/// quality-gate thresholds, and module-specific modifiers.
/// </summary>
[ApiController]
[Route("api/dialectics")]
public class DialecticsConfigController(
    IDialecticsModuleRegistry registry,
    IEngineConfigService engineConfig)
    : ControllerBase
{
    private static readonly HashSet<string> ValidFrameworks =
        new(StringComparer.OrdinalIgnoreCase) { "philosophy", "research" };

    public record DialecticsConfigResponse
    {
        public required ModuleInfo[] Modules { get; init; }
        public required int MaxAscentLevels { get; init; }
        public required int MaxTurns { get; init; }
        public required int DefaultIntensity { get; init; }
        public required string AscentScorerMode { get; init; }
        public required float AscentBetweennessFloor { get; init; }
        public required QualityGateSummary QualityGates { get; init; }
    }

    public record QualityGateSummary
    {
        public required bool Enabled { get; init; }
        public required int MaxTurns { get; init; }
        public required double SimilarityThreshold { get; init; }
        public required double QualityScoreExitThreshold { get; init; }
    }

    /// <summary>
    /// Returns runtime dialectics configuration for a framework.
    /// Pass <c>?framework=philosophy|research</c>.
    /// </summary>
    /// <remarks>
    /// Returns <see cref="IResult"/> so the response body is byte-identical to the minimal-API
    /// version this replaces (a bare JSON string on the bad-request paths).
    /// </remarks>
    [HttpGet("config")]
    public async Task<IResult> GetDialecticsConfig(
        [FromQuery] string framework,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(framework))
            return TypedResults.BadRequest("framework is required. Valid values: philosophy, research.");

        if (!ValidFrameworks.Contains(framework))
            return TypedResults.BadRequest(
                $"Unknown framework '{framework}'. Valid values: philosophy, research.");

        var modules = registry.GetAvailableModules()
            .Select(key =>
            {
                var module = registry.GetModule(key);
                return new ModuleInfo(key, module.DisplayName, module.Description);
            })
            .ToArray();

        var qg = await engineConfig.GetQualityGateOptionsAsync(framework, ct);
        var dial = await engineConfig.GetDialecticsOptionsAsync(framework, ct);

        var response = new DialecticsConfigResponse
        {
            Modules = modules,
            MaxAscentLevels = dial.MaxAscentLevels,
            MaxTurns = dial.MaxTurns,
            DefaultIntensity = dial.DefaultIntensity,
            AscentScorerMode = dial.AscentScorerMode.ToString(),
            AscentBetweennessFloor = dial.AscentBetweennessFloor,
            QualityGates = new QualityGateSummary
            {
                Enabled = qg.Enabled,
                MaxTurns = qg.MaxTurns,
                SimilarityThreshold = qg.SimilarityThreshold,
                QualityScoreExitThreshold = qg.QualityScoreExitThreshold,
            },
        };

        return TypedResults.Ok(response);
    }
}
