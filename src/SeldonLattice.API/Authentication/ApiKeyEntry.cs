namespace SeldonLattice.API.Authentication;

/// <summary>
/// Configuration entry for a single API key with associated roles.
/// </summary>
public sealed record ApiKeyEntry(string Key, string[] Roles);