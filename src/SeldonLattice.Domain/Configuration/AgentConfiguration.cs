namespace SeldonLattice.Domain.Configuration;

/// <summary>
/// Represents a single agent's configuration within a module (name, role, display color).
/// </summary>
public class AgentConfiguration
{
    /// <summary>Agent name (e.g., "proposer", "skeptic").</summary>
    public required string Name { get; init; }
    /// <summary>Framework/school (e.g., "stoicism").</summary>
    public string? Framework { get; init; }
    /// <summary>Description of the agent's perspective.</summary>
    public string? Description { get; init; }
    /// <summary>Communication style (e.g., "analytical", "poetic").</summary>
    public string? Style { get; init; }
    /// <summary>Areas of expertise.</summary>
    public string[]? Expertise { get; init; }
    public string Role { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}