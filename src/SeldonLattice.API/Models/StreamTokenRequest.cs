namespace SeldonLattice.API.Models;

/// <summary>
/// Request body for issuing a streaming token.
/// </summary>
/// <param name="SessionId">The session the token will be scoped to.</param>
/// <param name="Module">The module key the token authorizes (e.g. "philosophy", "research").</param>
public sealed record StreamTokenRequest(string SessionId, string? Module = "philosophy");
