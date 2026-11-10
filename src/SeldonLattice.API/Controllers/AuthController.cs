using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeldonLattice.API.Authentication;
using SeldonLattice.API.Models;

namespace SeldonLattice.API.Controllers;

/// <summary>
/// Controller for authentication and authorization endpoints.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(StreamTokenService tokenService, ILogger<AuthController> logger)
    : ControllerBase
{
    /// <summary>
    /// Validates the current API key and returns the authenticated principal's roles.
    /// </summary>
    [HttpGet("validate")]
    [Authorize(Policy = "AnyAuthenticated")]
    public IActionResult ValidateAuth()
    {
        var apiKey = User.FindFirst("api_key")?.Value ?? string.Empty;
        var roles = User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(r => r.Value).ToArray();
        var isAdmin = User.IsInRole("admin");

        return Ok(new
        {
            authenticated = true,
            apiKey,
            roles,
            isAdmin
        });
    }

    /// <summary>
    /// Issues a short-lived, single-use token for the SSE stream endpoints.
    /// </summary>
    /// <remarks>
    /// Requires the dedicated <c>stream</c> role as a plain role check (not the
    /// StreamOnly policy): minting must not be possible with a stream *token*,
    /// only with a key that carries the role.
    /// </remarks>
    [HttpPost("stream-token")]
    [Authorize(Roles = "stream")]
    public IActionResult IssueStreamToken([FromBody] StreamTokenRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.SessionId))
            return BadRequest(new { error = "SessionId is required" });

        if (string.IsNullOrWhiteSpace(req.Module))
            return BadRequest(new { error = "Module is required" });

        var token = tokenService.Issue(req.SessionId, req.Module);
        logger.LogInformation("Issued stream token for session {SessionId}, module {Module}", req.SessionId, req.Module);

        return Ok(new { streamToken = token });
    }
}
