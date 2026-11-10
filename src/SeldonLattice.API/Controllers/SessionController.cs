using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeldonLattice.API.Models;
using SeldonLattice.Application.Memory;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Infrastructure.Verification;

namespace SeldonLattice.API.Controllers;

/// <summary>
/// Endpoints for managing dialectical dialogue sessions.
/// All endpoints are scoped to the calling user. Authorization is granular:
/// reads require the <c>reader</c> role, mutations require <c>writer</c> -
/// per-action only, because policy attributes AND together if also applied
/// at class level.
/// </summary>
[ApiController]
[Route("api/sessions")]
public partial class SessionController(
    ISessionService sessionService,
    ICurrentUserService currentUser,
    IMerkleStore store,
    ISessionVerifier verifier,
    ExportVerifier exportVerifier,
    IClaimMemoryRetriever claimMemoryRetriever,
    ILogger<SessionController> logger)
    : ControllerBase
{
    /// <summary>
    /// Creates a new session for the calling user.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "WriterOnly")]
    public async Task<IResult> CreateSession(CreateSessionRequest req)
    {
        var ownerId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(ownerId))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(req.RootQuestion))
            return Results.BadRequest(new { error = "RootQuestion is required" });

        if (string.IsNullOrWhiteSpace(req.Framework))
            return Results.BadRequest(new { error = "Framework is required" });

        try
        {
            var sessionId = await sessionService.CreateSessionAsync(req.RootQuestion, req.Framework, ownerId, req.OverrideReason);
            return Results.Ok(new { sessionId, overrideReason = req.OverrideReason });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("LOUD WARNING"))
        {
            if (!string.IsNullOrWhiteSpace(req.OverrideReason))
            {
                var sessionId = await sessionService.CreateSessionAsync(req.RootQuestion, req.Framework, ownerId, req.OverrideReason);
                return Results.Ok(new { sessionId, overridden = true, reason = req.OverrideReason, loudWarning = ex.Message });
            }
            return Results.Conflict(new { error = "Hard stop blocked", message = ex.Message, rootQuestion = req.RootQuestion, framework = req.Framework, overrideRequired = true });
        }
    }

    /// <summary>
    /// Lists the calling user's sessions (Active by default), optionally filtered by framework/status.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "ReaderOnly")]
    public async Task<IResult> ListSessions(
        [FromQuery] string? framework,
        [FromQuery] string? status)
    {
        var ownerId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(ownerId))
            return Results.Unauthorized();

        SessionStatus? statusFilter = status?.ToLowerInvariant() switch
        {
            "active" => SessionStatus.Active,
            "archived" => SessionStatus.Archived,
            _ => null
        };

        var sessions = await sessionService.ListSessionsAsync(ownerId, framework, statusFilter);
        return Results.Ok(sessions);
    }

    /// <summary>
    /// Gets a single session owned by the calling user.
    /// </summary>
    [HttpGet("{sessionId}")]
    [Authorize(Policy = "ReaderOnly")]
    public async Task<IResult> GetSession(string sessionId)
    {
        var session = await GetOwnedSessionAsync(sessionId);
        return session == null ? Results.NotFound($"Session '{sessionId}' not found.") : Results.Ok(session);
    }

    /// <summary>
    /// Audit-safe soft delete: metadata is retained (status set to Archived)
    /// and the underlying Merkle nodes remain for tamper-evident history.
    /// </summary>
    [HttpDelete("{sessionId}")]
    [Authorize(Policy = "WriterOnly")]
    public async Task<IResult> DeleteSession(string sessionId)
    {
        var session = await GetOwnedSessionAsync(sessionId);
        if (session == null)
            return Results.NotFound($"Session '{sessionId}' not found.");

        await sessionService.SetSessionStatusAsync(sessionId, SessionStatus.Archived);
        return Results.Ok(new { sessionId, status = "archived" });
    }

    /// <summary>
    /// Hard-deletes all sessions for the calling user.
    /// </summary>
    [HttpDelete("all")]
    [Authorize(Policy = "WriterOnly")]
    public async Task<IResult> DeleteAllSessions()
    {
        var ownerId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(ownerId))
            return Results.Unauthorized();

        var deleted = await sessionService.DeleteAllSessionsAsync(ownerId);
        return Results.Ok(new { deleted, timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// Returns the full debate chain for a session, ordered chronologically.
    /// </summary>
    [HttpGet("{sessionId}/history")]
    [Authorize(Policy = "ReaderOnly")]
    public async Task<IResult> GetSessionHistory(string sessionId)
    {
        try
        {
            var session = await GetOwnedSessionAsync(sessionId);
            if (session == null)
                return Results.NotFound($"Session '{sessionId}' not found.");

            var nodes = await GetSessionNodesAsync(sessionId);

            var turns = nodes
                .Select((n, i) => MapToHistoryTurn(n, i + 1))
                .ToList();

            var history = new SessionHistory
            {
                SessionId = sessionId,
                RootQuestion = session.RootQuestion,
                Turns = turns
            };

            return Results.Ok(history);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve history for session {SessionId}", sessionId);
            return Results.Problem("An error occurred while retrieving session history.", statusCode: 500);
        }
    }

    /// <summary>
    /// Returns all structured claims extracted from the session's syntheses.
    /// </summary>
    [HttpGet("{sessionId}/claims")]
    [Authorize(Policy = "ReaderOnly")]
    public async Task<IResult> GetSessionClaims(string sessionId)
    {
        try
        {
            var session = await GetOwnedSessionAsync(sessionId);
            if (session == null)
                return Results.NotFound($"Session '{sessionId}' not found.");

            var claims = await store.GetAllNodesAsync<ArgumentClaimNode>();
            var sessionClaims = claims
                .Where(c => c.SessionId == sessionId)
                .OrderBy(c => c.Level)
                .ThenBy(c => c.Timestamp)
                .Select(c => new
                {
                    level = c.Level,
                    claim = c.ClaimText,
                    confidence = c.Confidence,
                    falsifiedBy = c.FalsifiedBy,
                    supportedBy = c.SupportedBy,
                    attacks = c.Attacks,
                    synthesisHash = c.ParentHashes.FirstOrDefault(),
                    timestamp = c.Timestamp,
                })
                .ToList();

            return Results.Ok(new
            {
                sessionId,
                rootQuestion = session.RootQuestion,
                totalClaims = sessionClaims.Count,
                claims = sessionClaims,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve claims for session {SessionId}", sessionId);
            return Results.Problem("An error occurred while retrieving session claims.", statusCode: 500);
        }
    }

    /// <summary>
    /// Returns a replayable representation of a session's dialectical ascent.
    /// </summary>
    [HttpGet("{sessionId}/replay")]
    [Authorize(Policy = "ReaderOnly")]
    public async Task<IResult> GetSessionReplay(string sessionId)
    {
        try
        {
            var session = await GetOwnedSessionAsync(sessionId);
            if (session == null)
                return Results.NotFound($"Session '{sessionId}' not found.");

            var nodes = await GetSessionNodesAsync(sessionId);

            var turns = nodes
                .Select((n, i) => MapToHistoryTurn(n, i + 1))
                .ToList();

            var replay = new SessionReplay
            {
                SessionId = sessionId,
                RootQuestion = session.RootQuestion,
                Module = session.Framework,
                Turns = turns,
                TotalTurns = turns.Count
            };

            return Results.Ok(replay);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate replay for session {SessionId}", sessionId);
            return Results.Problem("An error occurred while generating session replay.", statusCode: 500);
        }
    }

    /// <summary>
    /// Recall prior sessions' claims bearing on a new root question (cross-session claim memory).
    /// </summary>
    [HttpGet("/api/claims/memory")]
    [Authorize(Policy = "ReaderOnly")]
    public async Task<IResult> GetClaimMemory(
        [FromQuery] string? query,
        [FromQuery] string? excludeSession = null,
        [FromQuery] int? maxResults = null,
        [FromQuery] double? minSimilarity = null)
    {
        var ownerId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(ownerId))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(query))
            return Results.BadRequest(new { error = "query is required" });

        try
        {
            var options = new ClaimMemoryQueryOptions
            {
                MaxResults = Math.Clamp(maxResults ?? 5, 1, 50),
                MinSimilarity = (float)Math.Clamp(minSimilarity ?? 0.20, 0.01, 1.0),
                ExcludeSameSession = true,
            };

            var recalled = await claimMemoryRetriever.RetrieveAsync(query, excludeSession ?? string.Empty, options);

            return Results.Ok(new
            {
                query,
                excludeSession,
                recalled = recalled.Select(r => new
                {
                    level = r.Level,
                    sessionId = r.SessionId,
                    claim = r.Claim,
                    confidence = r.Confidence,
                    similarity = r.Similarity,
                    falsifiedBy = r.FalsifiedBy,
                    supportedBy = r.SupportedBy,
                }),
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Claim memory recall failed for query '{Query}'", query);
            return Results.Problem("An error occurred while recalling claims.", statusCode: 500);
        }
    }

    /// <summary>
    /// Verifies the cryptographic and structural integrity of a session's reasoning chain
    /// (Merkle CIDs, reference resolution, ML-DSA signatures).
    /// </summary>
    [HttpGet("{sessionId}/verify")]
    [Authorize(Policy = "ReaderOnly")]
    public async Task<IResult> VerifySession(string sessionId)
    {
        try
        {
            var session = await GetOwnedSessionAsync(sessionId);
            if (session == null)
                return Results.NotFound($"Session '{sessionId}' not found.");

            var report = await verifier.VerifySessionAsync(sessionId);

            return report.Passed
                ? Results.Ok(new
                {
                    sessionId,
                    verified = true,
                    nodesChecked = report.NodesChecked,
                    signaturesVerified = report.SignaturesVerified,
                    issues = report.Issues,
                })
                : Results.Ok(new
                {
                    sessionId,
                    verified = false,
                    nodesChecked = report.NodesChecked,
                    signaturesVerified = report.SignaturesVerified,
                    pointerIssues = report.PointerIssues,
                    signatureIssues = report.SignatureIssues,
                    issues = report.Issues,
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to verify session {SessionId}", sessionId);
            return Results.Problem("An error occurred while verifying the session.", statusCode: 500);
        }
    }

    /// <summary>
    /// Resolves a session scoped to the current user, returning null when the session
    /// does not exist or belongs to someone else.
    /// </summary>
    private async Task<Session?> GetOwnedSessionAsync(string sessionId)
    {
        var ownerId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(ownerId))
            return null;

        return await sessionService.GetUserSessionAsync(sessionId, ownerId);
    }

    private static string MapRole(string nodeType) => nodeType switch
    {
        // Shared
        "question" => "question",
        "mediation" => "mediation",
        "gate_rejection" => "gate_rejection",
        "synthesis" => "synthesis",

        // Philosophy module
        "proposer_reply" => "thesis",
        "skeptic_reply" => "antithesis",
        "mediator_synthesis" => "synthesis",
        "mediator" => "synthesis",

        // Fallback - pass through raw node type
        _ => nodeType
    };

    private async Task<List<DialecticalNode>> GetSessionNodesAsync(string sessionId)
    {
        var allNodes = await store.GetAllNodesAsync<DialecticalNode>();
        return
        [
            .. allNodes
                .Where(n => n.SessionId == sessionId && n.NodeType != "session_metadata")
                .OrderBy(n => n.Timestamp)
        ];
    }

    private static HistoryTurn MapToHistoryTurn(DialecticalNode node, int turnNumber) => new()
    {
        TurnNumber = turnNumber,
        Speaker = string.IsNullOrEmpty(node.SpeakerId) ? "system" : node.SpeakerId,
        Role = MapRole(node.NodeType),
        Content = node.Content,
        Timestamp = node.Timestamp,
        // The node's own gate score. This previously reported VariabilityScore, a
        // different quantity (semantic movement between turns), which made every
        // history turn read as "quality" while carrying something else entirely.
        QualityScore = node.QualityScore,
        Level = node.Level,
        AscentScore = node.AscentScore,
        JoinMediation = node.JoinMediation,
        JoinNovelty = node.JoinNovelty,
        IsSustained = node.IsSustained,
        IrreducibleTension = node.IrreducibleTension ?? 0f,
        ReasoningTrace = node.ReasoningTrace,
        RemainingDissensus = node.RemainingDissensus,
        RepeatsPremise = node.RepeatsPremise,
        UnresolvedTensions = node.UnresolvedTensions,
        TensionIntensity = node.TensionIntensity,
        CeremonyScore = node.CeremonyScore,
        SimilarityToPrevious = node.SimilarityToPrevious,
        NewArgumentCount = node.NewArgumentCount,
        WordCount = node.WordCount,
        HasFarewell = node.HasFarewell,
        HasAgreement = node.HasAgreement,
        HasDisagreement = node.HasDisagreement,
        SelfSimilarity = node.SelfSimilarity
    };
}
