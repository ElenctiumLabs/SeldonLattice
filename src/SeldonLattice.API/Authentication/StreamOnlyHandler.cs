using Microsoft.AspNetCore.Authorization;

namespace SeldonLattice.API.Authentication;

/// <summary>
/// Validates the <see cref="StreamOnlyRequirement"/>: accepts an authenticated principal
/// holding the <c>stream</c> role, or a valid streamToken query parameter bound to the
/// request's sessionId + module.
/// </summary>
/// <remarks>
/// Pre-hardening this handler accepted <em>any</em> authenticated principal (finding F5
/// in docs/audits/api-hardening-2026-10-20.md); it now requires the stream role explicitly
/// so that holding an unrelated key (e.g. settings-only) does not grant stream access.
/// </remarks>
public sealed class StreamOnlyHandler(StreamTokenService tokenService, IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<StreamOnlyRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        StreamOnlyRequirement requirement)
    {
        // Authenticated path: the key must carry the dedicated stream role.
        if (context.User.Identity?.IsAuthenticated == true)
        {
            if (context.User.IsInRole("stream"))
                context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            return Task.CompletedTask;

        if (!httpContext.Request.Query.TryGetValue("streamToken", out var tokenValue) ||
            string.IsNullOrEmpty(tokenValue))
        {
            return Task.CompletedTask;
        }

        var sessionId = httpContext.Request.Query["sessionId"].ToString();
        var module = httpContext.Request.Query["module"].ToString();
        if (string.IsNullOrEmpty(module))
            module = "classical";

        if (tokenService.TryValidate(tokenValue.ToString(), sessionId, module))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
