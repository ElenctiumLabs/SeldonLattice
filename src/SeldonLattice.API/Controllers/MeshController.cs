using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.API.Controllers;

/// <summary>
/// Endpoints for the Mesh of Knowing - 3D visualization tool.
/// Exposes embeddings and self-contained export bundles.
/// </summary>
[ApiController]
[Route("api")]
public class MeshController(
    IMerkleStore store,
    ISessionService sessionService,
    ICurrentUserService currentUser)
    : ControllerBase
{
    /// <summary>
    /// Returns all node embeddings for a session as { cid, vector } pairs.
    /// </summary>
    [HttpGet("nodes/embeddings")]
    [Authorize(Policy = "ReaderOnly")]
    public async Task<IResult> GetEmbeddings([FromQuery] string? sessionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return Results.BadRequest(new { error = "sessionId is required" });

        var ownerId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(ownerId))
            return Results.Unauthorized();

        var session = await sessionService.GetUserSessionAsync(sessionId, ownerId);
        if (session == null)
            return Results.NotFound($"Session '{sessionId}' not found.");

        var allNodes = await store.GetAllNodesAsync<DialecticalNode>();
        var sessionNodes = allNodes
            .Where(n => n.SessionId == sessionId && n.NodeType != "session_metadata")
            .OrderBy(n => n.Timestamp)
            .ThenBy(n => n.GetCid().ToString())
            .ToList();

        var embeddings = sessionNodes
            .Where(n => n.Embedding is { Length: > 0 })
            .Select(n => new { cid = n.GetCid().ToString(), vector = n.Embedding })
            .ToList();

        return Results.Ok(new
        {
            sessionId,
            count = embeddings.Count,
            embeddings
        });
    }

    /// <summary>
    /// Returns a self-contained bundle: nodes + metadata + embeddings + lineage edges.
    /// </summary>
    [HttpGet("mesh/export")]
    [Authorize(Policy = "ReaderOnly")]
    public async Task<IResult> ExportMesh(
        [FromQuery] string? sessionId,
        [FromQuery] string format = "json",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return Results.BadRequest(new { error = "sessionId is required" });

        var ownerId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(ownerId))
            return Results.Unauthorized();

        var session = await sessionService.GetUserSessionAsync(sessionId, ownerId);
        if (session == null)
            return Results.NotFound($"Session '{sessionId}' not found.");

        var allNodes = await store.GetAllNodesAsync<DialecticalNode>();
        var sessionNodes = allNodes
            .Where(n => n.SessionId == sessionId && n.NodeType != "session_metadata")
            .OrderBy(n => n.Timestamp)
            .ThenBy(n => n.GetCid().ToString())
            .ToList();

        var nodes = sessionNodes.Select(n => new
        {
            cid = n.GetCid().ToString(),
            nodeType = n.NodeType,
            level = n.Level,
            parentHash = n.ParentHash,
            parentHashes = n.ParentHashes,
            content = n.Content,
            cyclePhase = n.CyclePhase,
            sessionId = n.SessionId,
            speakerId = n.SpeakerId,
            timestamp = n.Timestamp,
            qualityScore = n.QualityScore,
            ceremonyScore = n.CeremonyScore,
            similarityToPrevious = n.SimilarityToPrevious,
            ascentScore = n.AscentScore,
            irreducibleTension = n.IrreducibleTension,
            remainingDissensus = n.RemainingDissensus,
            repeatsPremise = n.RepeatsPremise,
            unresolvedTensions = n.UnresolvedTensions,
            tensionIntensity = n.TensionIntensity,
            isSecondaryCenter = n.IsSecondaryCenter,
            centerShiftScore = n.CenterShiftScore,
            moderatorSelectionConfidence = n.ModeratorSelectionConfidence,
            reasoningTrace = n.ReasoningTrace,
            newArgumentCount = n.NewArgumentCount,
            wordCount = n.WordCount,
            hasFarewell = n.HasFarewell,
            hasAgreement = n.HasAgreement,
            hasDisagreement = n.HasDisagreement,
            selfSimilarity = n.SelfSimilarity,
            embedding = n.Embedding
        }).ToList();

        // Build lineage edges from parentHash/parentHashes → cid
        var edges = new List<object>();
        foreach (var n in sessionNodes)
        {
            var parents = n.ParentHashes ?? (n.ParentHash != null ? [n.ParentHash] : null);
            if (parents == null) continue;
            foreach (var p in parents)
            {
                edges.Add(new { from = p, to = n.GetCid().ToString() });
            }
        }

        var bundle = new
        {
            sessionId,
            framework = session.Framework,
            rootQuestion = session.RootQuestion,
            exportedAt = DateTime.UtcNow,
            nodeCount = nodes.Count,
            nodes,
            edges
        };

        return Results.Ok(bundle);
    }
}
