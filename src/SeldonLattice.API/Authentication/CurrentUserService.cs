using System.Security.Claims;
using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.API.Authentication;

/// <summary>
/// Resolves the current user's identity from the authentication principal.
/// Reads <see cref="ClaimTypes.NameIdentifier"/>, which the API key handler populates
/// with the key for now; under SSO it will be the provider subject - callers are
/// insulated either way.
/// </summary>
public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    /// <inheritdoc />
    public string? UserId =>
        httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
}