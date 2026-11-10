namespace SeldonLattice.API.Models;

/// <summary>
/// Health check response for a single dependency.
/// </summary>
public sealed record DependencyHealth(string Name, string Status, string? Detail = null);