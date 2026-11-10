using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Service for managing dialectical dialogue sessions.
/// Sessions track the progression of philosophical inquiry through multiple ascent levels
/// and are scoped to a framework (module) and an owning user.
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Creates a new session for tracking a dialectical inquiry.
    /// </summary>
    /// <param name="rootQuestion">The initial philosophical question to seed the ascent.</param>
    /// <param name="framework">The dialectical framework (module) this session belongs to.</param>
    /// <param name="ownerId">Identity of the owning user.</param>
    /// <returns>The unique session ID.</returns>
    Task<string> CreateSessionAsync(string rootQuestion, string framework, string ownerId, string? overrideReason = null);

    /// <summary>
    /// Retrieves a session by ID regardless of ownership.
    /// Intended for internal/server-side resolution (e.g. root-theme lookups in the
    /// ascent services). Public API paths must use <see cref="GetUserSessionAsync"/>.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns>The Session if found, or null if not found.</returns>
    Task<Session?> GetSessionAsync(string sessionId);

    /// <summary>
    /// Retrieves a session by ID scoped to an owner. Returns null when the session
    /// does not exist or is owned by a different user.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="ownerId">The owning user's identity.</param>
    /// <returns>The Session if found and owned, otherwise null.</returns>
    Task<Session?> GetUserSessionAsync(string sessionId, string ownerId);

    /// <summary>
    /// Lists the sessions owned by a user, optionally filtered by framework and status.
    /// Active sessions only unless a status filter is supplied.
    /// </summary>
    /// <param name="ownerId">The owning user's identity.</param>
    /// <param name="framework">Optional framework filter.</param>
    /// <param name="status">Optional status filter (defaults to Active).</param>
    /// <returns>Sessions ordered by creation date (newest first).</returns>
    Task<IEnumerable<Session>> ListSessionsAsync(string ownerId, string? framework = null, SessionStatus? status = null);

    /// <summary>
    /// Lists every session across all owners. Internal tooling only (administrative tooling,
    /// offline diagnostics) - public API paths must stay owner-scoped.
    /// </summary>
    /// <returns>All sessions ordered by creation date (newest first).</returns>
    Task<IEnumerable<Session>> ListAllSessionsAsync();

    /// <summary>
    /// Updates session metadata after a dialogue level completes.
    /// </summary>
    /// <param name="sessionId">The session to update.</param>
    /// <param name="nodeCount">Updated node count.</param>
    /// <param name="maxLevel">Updated maximum level reached.</param>
    Task UpdateSessionAsync(string sessionId, int nodeCount, int maxLevel);

    /// <summary>
    /// Updates the lifecycle status of a session (e.g. archive). Rows and nodes are
    /// retained for audit.
    /// </summary>
    /// <param name="sessionId">The session to update.</param>
    /// <param name="status">The new lifecycle state.</param>
    Task SetSessionStatusAsync(string sessionId, SessionStatus status);

    /// <summary>
    /// Hard-deletes all sessions owned by a user from the database.
    /// Returns the number of sessions deleted.
    /// </summary>
    /// <param name="ownerId">The owning user's identity.</param>
    Task<int> DeleteAllSessionsAsync(string ownerId);
}