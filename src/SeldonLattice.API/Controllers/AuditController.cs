using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.API.Controllers;

/// <summary>
/// Endpoints for querying LLM audit records stored in the Merkle DAG.
/// </summary>
[ApiController]
[Route("api/audit")]
public class AuditController(
    IMerkleStore store,
    ICurrentUserService currentUser,
    ISessionService sessionService)
    : ControllerBase
{
    /// <summary>
    /// Retrieves all LLM audit nodes for a session, ordered by Timestamp ascending.
    /// </summary>
    [HttpGet("{sessionId}")]
    [Authorize(Policy = "ReaderOnly")]
    public async Task<IActionResult> GetAuditNodes(
        string sessionId,
        [FromQuery] string? flagged,
        [FromQuery] string? module)
    {
        // 1. Validate UUID format (Requirement 7.8)
        if (!Guid.TryParse(sessionId, out _))
        {
            return BadRequest(new { error = "sessionId must be a valid UUID" });
        }

        // 2. Authentication check (Requirement 7.7)
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        // 3. Ownership check (Requirement 7.2)
        var session = await sessionService.GetSessionAsync(sessionId);
        if (session == null || session.OwnerId != userId)
        {
            return Forbid();
        }

        // 4. Stream and filter audit records (Requirements 7.1, 7.3, 7.4, 7.5, 7.6)
        var isFlaggedFilter = string.Equals(flagged, "true", StringComparison.OrdinalIgnoreCase);
        var hasFlaggedParam = !string.IsNullOrWhiteSpace(flagged);
        var isAdmin = User.IsInRole("admin");

        var nodes = new List<object>();
        await foreach (var node in store.GetAuditNodesAsync(sessionId))
        {
            if (hasFlaggedParam && isFlaggedFilter && !node.IsFlagged)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(module) &&
                !string.Equals(node.ModuleKey, module, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            nodes.Add(new
            {
                node.NodeType,
                node.SessionId,
                node.SpeakerId,
                node.ModuleKey,
                node.Level,
                node.Turn,
                PromptMessages = isAdmin ? node.PromptMessages : null,
                node.CompletionText,
                node.InputTokenCount,
                node.OutputTokenCount,
                node.ModelId,
                node.LatencyMs,
                node.Timestamp,
                node.ParentCid,
                node.QualityFlags,
                node.IsFlagged
            });
        }

        return Ok(nodes);
    }
}
