namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Resolves the identity of the calling user for owner-scoped operations.
///
/// Currently backed by the API key authentication claim (ClaimTypes.NameIdentifier).
/// When SSO lands, this resolves to the provider subject instead - endpoints and
/// services read <see cref="UserId"/> exclusively and need no changes.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>Id of the current user, or null when the request is anonymous.</summary>
    string? UserId { get; }
}