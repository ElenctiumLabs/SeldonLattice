using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Infrastructure.Sessions;

/// <summary>
/// SQLite-backed manager for dialectical dialogue sessions (single source of truth).
///
/// Session metadata is persisted via EF Core; dialectical nodes remain in the Merkle
/// DAG for tamper-evident auditing. Sessions are scoped to a framework (module) and an
/// owning user, and lifecycle states (Active/Archived) allow audit-safe archive/deletion.
///
/// A <see cref="IDbContextFactory{SessionsDbContext}"/> is used (rather than a scoped
/// context) because the ascent services resolve session metadata while SSE streaming,
/// which outlives the HTTP request scope. Each operation therefore gets a short-lived
/// context instead of a request-scoped one.
/// </summary>
public class SessionService(
    IDbContextFactory<SessionsDbContext> dbFactory,
    ILogger<SessionService> logger) : ISessionService
{
    /// <inheritdoc />
    public async Task<string> CreateSessionAsync(string rootQuestion, string framework, string ownerId, string? overrideReason = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var session = new Session
        {
            SessionId = Guid.NewGuid().ToString(),
            Framework = framework.ToLowerInvariant(),
            RootQuestion = rootQuestion,
            OwnerId = ownerId,
            Status = SessionStatus.Active,
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            NodeCount = 0,
            MaxLevel = 0
        };

        // Hard-stop guard: block repetition of a sustained/hard-stopped root theme in this framework.
        await using var checkDb = await dbFactory.CreateDbContextAsync();
        var existingHardStop = await checkDb.Sessions
            .AsNoTracking()
            .AnyAsync(s => s.OwnerId == ownerId
                           && s.Framework == framework.ToLowerInvariant()
                           && s.RootQuestion == rootQuestion
                           && s.IsHardStopped == true);
        if (!string.IsNullOrWhiteSpace(overrideReason))
        {
            logger.LogWarning("LOUD WARNING - HARD STOP OVERRIDDEN by user. Reason: {Reason}. Stashed orphan artifacts prevented accidental merge.", overrideReason);
        }
        else if (existingHardStop)
        {
            throw new InvalidOperationException(
                "LOUD WARNING - HARD STOP BLOCKED: A session for root question '" + rootQuestion +
                "' in framework '" + framework + "' has already reached sustained/hard-stop (IsHardStopped=true). " +
                "Repetition blocked. Human check required before proceeding. " +
                "Stashed orphaned physics-engine artifacts (.stash/physics-engine/Noether.Orchestration) to prevent accidental merge.");
        }

        db.Sessions.Add(session);
        await db.SaveChangesAsync();

        logger.LogInformation("Created session {SessionId} for {Owner} in framework {Framework}: {Question}",
            session.SessionId, ownerId, session.Framework, rootQuestion);
        return session.SessionId;
    }

    /// <inheritdoc />
    public async Task<Session?> GetSessionAsync(string sessionId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Sessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);
    }

    /// <inheritdoc />
    public async Task<Session?> GetUserSessionAsync(string sessionId, string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Sessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.OwnerId == ownerId);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Session>> ListSessionsAsync(string ownerId, string? framework = null, SessionStatus? status = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var query = db.Sessions.AsNoTracking().Where(s => s.OwnerId == ownerId);

        if (!string.IsNullOrWhiteSpace(framework))
            query = query.Where(s => s.Framework == framework.ToLowerInvariant());

        query = query.Where(s => s.Status == (status ?? SessionStatus.Active));

        return await query.OrderByDescending(s => s.CreatedAt).ToListAsync();
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Session>> ListAllSessionsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Sessions
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task UpdateSessionAsync(string sessionId, int nodeCount, int maxLevel)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var session = await db.Sessions.FirstOrDefaultAsync(s => s.SessionId == sessionId);
        if (session is null)
        {
            logger.LogWarning("Cannot update session {SessionId}: not found", sessionId);
            return;
        }

        session.LastUpdatedAt = DateTime.UtcNow;
        session.NodeCount = nodeCount;
        session.MaxLevel = maxLevel;
        await db.SaveChangesAsync();

        logger.LogInformation("Updated session {SessionId}: NodeCount={NodeCount}, MaxLevel={MaxLevel}",
            sessionId, nodeCount, maxLevel);
    }

    /// <inheritdoc />
    public async Task SetSessionStatusAsync(string sessionId, SessionStatus status)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var session = await db.Sessions.FirstOrDefaultAsync(s => s.SessionId == sessionId);
        if (session is null)
        {
            logger.LogWarning("Cannot set status on session {SessionId}: not found", sessionId);
            return;
        }

        session.Status = status;
        session.LastUpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        logger.LogInformation("Session {SessionId} status set to {Status}", sessionId, status);
    }

    /// <inheritdoc />
    public async Task<int> DeleteAllSessionsAsync(string ownerId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var sessions = await db.Sessions
            .Where(s => s.OwnerId == ownerId)
            .ToListAsync();

        if (sessions.Count == 0)
            return 0;

        db.Sessions.RemoveRange(sessions);
        var deleted = await db.SaveChangesAsync();

        logger.LogWarning("Hard-deleted {Count} sessions for owner {Owner}", deleted, ownerId);
        return deleted;
    }
}