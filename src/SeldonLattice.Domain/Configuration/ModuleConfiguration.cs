namespace SeldonLattice.Domain.Configuration;

/// <summary>
/// Definition for creating a custom dialectics module at runtime.
/// </summary>
public sealed record ModuleConfiguration
{
    /// <summary>Unique module key used for registration and lookup.</summary>
    public required string Key { get; init; }
    /// <summary>Human-readable name for UI display.</summary>
    public required string Name { get; init; }
    /// <summary>Description of the module's purpose.</summary>
    public string? Description { get; init; }
    /// <summary>Agents participating in the dialectic.</summary>
    // public required CustomAgentConfiguration[] Agents { get; init; }
    public AgentConfiguration[] Agents { get; init; } = [];
    /// <summary>Quality gate names to apply.</summary>
    public string[]? QualityGates { get; init; }
    /// <summary>Optional mediator configuration.</summary>
    public MediatorConfigudation? MediatorConfig { get; init; }
    /// <summary>Optional moderator configuration.</summary>
    public ModeratorConfiguration? ModeratorConfig { get; init; }
    
     public DateTime CreatedAt { get; set; }
}