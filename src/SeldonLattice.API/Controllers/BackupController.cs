using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeldonLattice.API.Models;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.API.Controllers;

/// <summary>
/// Backup and restore endpoints for the Merkle DAG storage.
/// Exports all dialectical nodes as JSON and supports re-importing them.
/// </summary>
[ApiController]
[Route("api/admin/backup")]
[Authorize(Policy = "AdminOnly")]
public class BackupController(IMerkleStore store, ILogger<BackupController> logger) : ControllerBase
{
    private readonly IMerkleStore _store = store;
    private readonly ILogger<BackupController> _logger = logger;

    private static async Task<IResult> ExportBackup(
        IMerkleStore store,
        [FromServices] ILogger logger)
    {
        try
        {
            var allNodes = await store.GetAllNodesAsync<DialecticalNode>();
            var nodesList = allNodes.ToList();
            var backup = new BackupData
            {
                ExportedAt = DateTime.UtcNow,
                NodeCount = nodesList.Count,
                Nodes =
                [
                    .. nodesList.Select(n => new BackupNode
                    {
                        Cid = n.GetCid().ToString(),
                        NodeType = n.NodeType,
                        Level = n.Level,
                        Content = n.Content,
                        SessionId = n.SessionId,
                        SpeakerId = n.SpeakerId,
                        Timestamp = n.Timestamp,
                        VariabilityScore = n.VariabilityScore,
                        ParentHash = n.ParentHash,
                        ParentHashes = n.ParentHashes,
                        ParentPatternHash = n.ParentPatternHash,
                        IsSecondaryCenter = n.IsSecondaryCenter,
                        CenterShiftScore = n.CenterShiftScore,
                        CyclePhase = n.CyclePhase,
                        ResonanceScore = n.ResonanceScore,
                        ModeratorSelectionConfidence = n.ModeratorSelectionConfidence
                    })
                ]
            };

            var json = System.Text.Json.JsonSerializer.Serialize(backup, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            return Results.File(
                System.Text.Encoding.UTF8.GetBytes(json),
                "application/json",
                $"seldon-lattice-backup-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.json");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backup export failed");
            return Results.Problem("Backup export failed.", statusCode: 500);
        }
    }

    private static async Task<IResult> ImportBackup(
        IMerkleStore store,
        [FromBody] BackupData? backup,
        [FromServices] ILogger logger)
    {
        try
        {
            if (backup?.Nodes == null || backup.Nodes.Count == 0)
            {
                return Results.BadRequest("No nodes found in backup data.");
            }

            int imported = 0;
            int skipped = 0;

            foreach (var nodeDto in backup.Nodes)
            {
                try
                {
                    var node = new DialecticalNode
                    {
                        NodeType = nodeDto.NodeType,
                        Level = nodeDto.Level,
                        Content = nodeDto.Content,
                        SessionId = nodeDto.SessionId,
                        SpeakerId = nodeDto.SpeakerId,
                        Timestamp = nodeDto.Timestamp,
                        VariabilityScore = nodeDto.VariabilityScore,
                        ParentHash = nodeDto.ParentHash,
                        ParentHashes = nodeDto.ParentHashes,
                        ParentPatternHash = nodeDto.ParentPatternHash,
                        IsSecondaryCenter = nodeDto.IsSecondaryCenter,
                        CenterShiftScore = nodeDto.CenterShiftScore,
                        CyclePhase = nodeDto.CyclePhase,
                        ResonanceScore = nodeDto.ResonanceScore,
                        ModeratorSelectionConfidence = nodeDto.ModeratorSelectionConfidence
                    };

                    await store.PutBlobAsync(node);
                    imported++;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to import node");
                    skipped++;
                }
            }

            logger.LogInformation("Backup import completed: {Imported} imported, {Skipped} skipped", imported, skipped);

            return Results.Ok(new
            {
                status = "imported",
                imported,
                skipped,
                total = backup.Nodes.Count,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backup import failed");
            return Results.Problem("Backup import failed.", statusCode: 500);
        }
    }
}