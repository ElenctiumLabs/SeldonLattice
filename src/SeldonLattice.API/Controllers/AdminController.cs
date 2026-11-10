using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SeldonLattice.Domain.Configuration;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Domain.Options;

namespace SeldonLattice.API.Controllers;

/// <summary>
/// Admin endpoints for database migrations, seed data, and status checks.
/// Since Seldon Lattice uses FASTER (not a relational DB), migrations are
/// idempotent operations that ensure default configuration exists in the Merkle store.
/// </summary>
/// <remarks>
/// All routes require the <c>admin</c> role.
/// </remarks>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminOnly")]
public class AdminController(
    IMerkleStore store,
    IOptions<QualityGateOptions> qualityGateOptions,
    IOptions<SeedDataOptions> seedDataOptions,
    IConfiguration configuration,
    ILogger<AdminController> logger)
    : ControllerBase
{
    /// <summary>
    /// Applies pending migrations (ensures default dialectics configuration exists in the Merkle store).
    /// </summary>
    [HttpGet("migrate")]
    public async Task<IResult> Migrate()
    {
        try
        {
            var result = await EnsureDefaultConfigurationAsync(store, qualityGateOptions, seedDataOptions, logger);
            return Results.Ok(new
            {
                status = "migrated",
                applied = result.Applied,
                skipped = result.Skipped,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Migration failed");
            return Results.Problem("Migration failed.", statusCode: 500);
        }
    }

    /// <summary>
    /// Seeds default dialectics configuration (classical module, quality gates) into the Merkle store.
    /// </summary>
    [HttpPost("seed")]
    public async Task<IResult> Seed()
    {
        try
        {
            int nodesCreated = await SeedDefaultConfigurationAsync(store, qualityGateOptions, seedDataOptions, logger);
            return Results.Ok(new
            {
                status = "seeded",
                nodesCreated,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Seeding failed");
            return Results.Problem("Seeding failed.", statusCode: 500);
        }
    }

    /// <summary>
    /// Returns the current migration/seed status and Merkle store health.
    /// </summary>
    [HttpGet("status")]
    public async Task<IResult> AdminStatus()
    {
        try
        {
            var allNodes = await store.GetAllNodesAsync<DialecticalNode>();
            var dialecticalNodes = allNodes as DialecticalNode[] ?? [.. allNodes];
            var sessionNodes = dialecticalNodes.Count(n => n.NodeType == "session_metadata");
            var totalNodes = dialecticalNodes.Count();

            var hasPhilosophyConfig = dialecticalNodes.Any(n =>
                n.NodeType == "module_config" && n.Content.Contains("philosophy"));
            var hasQualityGates = dialecticalNodes.Any(n =>
                n.NodeType == "quality_gate_config");

            return Results.Ok(new
            {
                status = "healthy",
                merkleStore = new
                {
                    totalNodes,
                    sessionNodes,
                    healthy = true
                },
                configuration = new
                {
                    philosophyModuleConfigured = hasPhilosophyConfig,
                    qualityGatesConfigured = hasQualityGates,
                    migrationsApplied = hasPhilosophyConfig
                },
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Admin status check failed");
            return Results.Problem("Status check failed.", statusCode: 500);
        }
    }


    /// <summary>
    /// Clears Merkle store data and PQC keys from the data directory.
    /// </summary>
    [HttpDelete("data")]
    public async Task<IResult> ClearData()
    {
        try
        {
            var dataDir = configuration["DataDir"] ?? "data";

            await store.ClearAsync();
            logger.LogWarning("Merkle store cleared");

            var pqcKeysDir = Path.Combine(dataDir, "pqc-keys");
            int keysDeleted = 0;
            if (Directory.Exists(pqcKeysDir))
            {
                var keyFiles = Directory.GetFiles(pqcKeysDir, "*.mldsa.json");
                foreach (var keyFile in keyFiles)
                {
                    try
                    {
                        System.IO.File.Delete(keyFile);
                        keysDeleted++;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to delete PQC key {Path}", keyFile);
                    }
                }
            }

            return Results.Ok(new
            {
                status = "cleared",
                merkleCleared = true,
                keysDeleted,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Data clear failed");
            return Results.Problem("Data clear failed.", statusCode: 500);
        }
    }

    private static async Task<(bool Applied, int Skipped)> EnsureDefaultConfigurationAsync(
        IMerkleStore store, IOptions<QualityGateOptions> qualityGateOptions, IOptions<SeedDataOptions> seedDataOptions, ILogger logger)
    {
        var allNodes = await store.GetAllNodesAsync<DialecticalNode>();
        var applied = 0;
        var skipped = 0;

        // Ensure philosophy module config exists
        var dialecticalNodes = allNodes as DialecticalNode[] ?? [.. allNodes];
        if (!dialecticalNodes.Any(n => n.NodeType == "module_config" && n.Content.Contains("philosophy")))
        {
            await SeedPhilosophyModuleConfig(store, seedDataOptions);
            applied++;
            logger.LogInformation("Applied migration: philosophy module configuration seeded");
        }
        else
        {
            skipped++;
        }

        // Ensure quality gate config exists
        if (dialecticalNodes.All(n => n.NodeType != "quality_gate_config"))
        {
            await SeedQualityGateConfig(store, qualityGateOptions);
            applied++;
            logger.LogInformation("Applied migration: quality gate configuration seeded");
        }
        else
        {
            skipped++;
        }

        return (applied > 0, skipped);
    }

    private static async Task<int> SeedDefaultConfigurationAsync(
        IMerkleStore store, IOptions<QualityGateOptions> qualityGateOptions, IOptions<SeedDataOptions> seedDataOptions, ILogger logger)
    {
        int count = 0;
        await SeedPhilosophyModuleConfig(store, seedDataOptions);
        count++;
        await SeedQualityGateConfig(store, qualityGateOptions);
        count++;
        logger.LogInformation("Seeded all default dialectics configuration");
        return count;
    }

    private static async Task SeedPhilosophyModuleConfig(IMerkleStore store, IOptions<SeedDataOptions> seedDataOptions)
    {
        var colors = seedDataOptions.Value.AgentColors;
        var config = new ModuleConfiguration
        {
            Key = "philosophy",
            Name = "Philosophical Reasoning",
            Description = "Proposer and Skeptic debate with Mediator moderation - the faculty-agnostic philosophical reasoning module.",
            Agents =
            [
                new AgentConfiguration { Name = "Proposer", Role = "thesis", Color = colors.GetValueOrDefault("proposer", "#11b880") },
                new AgentConfiguration { Name = "Skeptic", Role = "antithesis", Color = colors.GetValueOrDefault("skeptic", "#4ECDC4") },
                new AgentConfiguration { Name = "Mediator", Role = "synthesis", Color = colors.GetValueOrDefault("mediator", "#FF6B6B") }
            ],
            CreatedAt = DateTime.UtcNow
        };
        await store.PutBlobAsync(new DialecticalNode
        {
            NodeType = "module_config",
            Level = 0,
            Content = System.Text.Json.JsonSerializer.Serialize(config),
            SessionId = seedDataOptions.Value.PhilosophySessionId,
            Timestamp = DateTime.UtcNow
        });
    }

    private static async Task SeedQualityGateConfig(IMerkleStore store, IOptions<QualityGateOptions> qualityGateOptions)
    {
        var qg = qualityGateOptions.Value;
        var config = new QualityGateConfiguration
        {
            Enabled = qg.Enabled,
            MaxTurns = qg.MaxTurns,
            ConsecutiveFarewellsThreshold = qg.ConsecutiveFarewellsThreshold,
            SimilarityThreshold = qg.SimilarityThreshold,
            AgreementStagnationTurns = qg.AgreementStagnationTurns,
            CeremonyScoreWarning = qg.CeremonyScoreWarning,
            VerboseWordCount = qg.VerboseWordCount,
            ContentRepetitionWarning = qg.ContentRepetitionWarning,
            QualityScoreExitThreshold = qg.QualityScoreExitThreshold,
            LowQualityConsecutiveTurns = qg.LowQualityConsecutiveTurns,
            CircularRestatementThreshold = qg.CircularRestatementThreshold,
            PremiseForkDistanceThreshold = qg.PremiseForkDistanceThreshold,
            PremiseForkSustainedTurns = qg.PremiseForkSustainedTurns,
            UpdatedAt = DateTime.UtcNow
        };
        await store.PutBlobAsync(new DialecticalNode
        {
            NodeType = "quality_gate_config",
            Level = 0,
            Content = System.Text.Json.JsonSerializer.Serialize(config),
            SessionId = "config-quality-gates",
            Timestamp = DateTime.UtcNow
        });
    }
}
