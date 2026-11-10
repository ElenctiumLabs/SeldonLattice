using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeldonLattice.API.Models;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Domain.Options;

namespace SeldonLattice.API.Controllers;

/// <summary>
/// Endpoints for retrieving dialectical nodes from the Merkle store.
/// </summary>
[ApiController]
[Route("api/nodes")]
public class NodeController(
    IMerkleStore store,
    ISessionService sessionService,
    ICurrentUserService currentUser,
    ILogger<NodeController> logger)
    : ControllerBase
{
    /// <summary>
    /// Returns paginated, filterable dialectical nodes from the Merkle store.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "ReaderOnly")]
    public async Task<IResult> GetNodes(
        [FromQuery] string? sessionId,
        [FromQuery] int? level,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var effectivePage = page ?? 1;
        var effectivePageSize = pageSize ?? DefaultsOptions.DefaultPage;
        if (effectivePageSize < 1) effectivePageSize = 50;
        if (effectivePageSize > 200) effectivePageSize = 200;
        if (effectivePage < 1) effectivePage = 1;

        try
        {
            // Session-scoped reads are restricted to the owning user so users cannot
            // harvest another user's dialogue nodes by guessing a session id.
            if (!string.IsNullOrEmpty(sessionId))
            {
                var ownerId = currentUser.UserId;
                if (string.IsNullOrWhiteSpace(ownerId))
                    return Results.Unauthorized();

                var session = await sessionService.GetUserSessionAsync(sessionId, ownerId);
                if (session == null)
                    return Results.NotFound($"Session '{sessionId}' not found.");
            }

            var allNodes = await store.GetAllNodesAsync<DialecticalNode>();

            var filtered = allNodes.Where(n => n.NodeType != "session_metadata");

            if (!string.IsNullOrEmpty(sessionId))
                filtered = filtered.Where(n => n.SessionId == sessionId);

            if (level.HasValue)
                filtered = filtered.Where(n => n.Level == level.Value);

            var dialecticalNodes = filtered as DialecticalNode[] ?? [.. filtered];
            var totalCount = dialecticalNodes.Length;
            // Deterministic ordering is required before Skip/Take: the Merkle
            // store enumerates nodes in dictionary order, so an un-ordered
            // pagination would scatter a session's question → thesis →
            // antithesis → synthesis sequence across a transcript/graph and
            // arbitrarily drop nodes on multi-page fetches.
            var paged = dialecticalNodes
                .Select(n => new { Node = n, Cid = n.GetCid().ToString() })
                .OrderBy(x => x.Node.Timestamp)
                .ThenBy(x => x.Cid)
                .Select(x => x.Node)
                .Skip((effectivePage - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .ToList();

            var result = new PagedResponse<Node>
            {
                Items = paged.Select(MapToDto),
                TotalCount = totalCount,
                Page = effectivePage,
                PageSize = effectivePageSize,
                TotalPages = (int)Math.Ceiling(totalCount / (double)effectivePageSize)
            };

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve nodes from Merkle store. sessionId={SessionId}, level={Level}", sessionId, level);
            return Results.Problem("An error occurred while retrieving nodes from the store.", statusCode: 500);
        }
    }

    private static Node MapToDto(DialecticalNode n) => new()
    {
        Cid = n.GetCid().ToString(),
        NodeType = n.NodeType,
        Level = n.Level,
        ParentHash = n.ParentHash,
        ParentHashes = n.ParentHashes,
        Content = n.Content,
        CyclePhase = n.CyclePhase,
        VariabilityScore = n.VariabilityScore,
        ResonanceScore = n.ResonanceScore,
        ParentPatternHash = n.ParentPatternHash,
        IsSecondaryCenter = n.IsSecondaryCenter,
        CenterShiftScore = n.CenterShiftScore,
        SessionId = n.SessionId,
        SpeakerId = n.SpeakerId,
        ModeratorSelectionConfidence = n.ModeratorSelectionConfidence,
        Timestamp = n.Timestamp,
        AuthorityReferences = n.AuthorityReferences,
        IsSustained = n.IsSustained,
        AscentScore = n.AscentScore,
        JoinMediation = n.JoinMediation,
        JoinNovelty = n.JoinNovelty,
        IrreducibleTension = n.IrreducibleTension ?? 0f,
        ReasoningTrace = n.ReasoningTrace,
        RemainingDissensus = n.RemainingDissensus,
        RepeatsPremise = n.RepeatsPremise,
        UnresolvedTensions = n.UnresolvedTensions,
        TensionIntensity = n.TensionIntensity,
        QualityScore = n.QualityScore,
        CeremonyScore = n.CeremonyScore,
        SimilarityToPrevious = n.SimilarityToPrevious,
        NewArgumentCount = n.NewArgumentCount,
        WordCount = n.WordCount,
        HasFarewell = n.HasFarewell,
        HasAgreement = n.HasAgreement,
        HasDisagreement = n.HasDisagreement,
        SelfSimilarity = n.SelfSimilarity
    };
}
