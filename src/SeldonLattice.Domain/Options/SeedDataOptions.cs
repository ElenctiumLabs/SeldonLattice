namespace SeldonLattice.Domain.Options;

/// <summary>
/// Configuration for seed data (agent colors, session IDs).
/// Read from "SeedData" section in appsettings.json.
/// </summary>
public class SeedDataOptions
{
    /// <summary>
    /// Agent display colors keyed by agent ID (faculty roles, not people).
    /// Used by seed endpoints and the frontend for consistent coloring.
    /// </summary>
    public Dictionary<string, string> AgentColors { get; set; } = new()
    {
        ["proposer"] = "#11b880",
        ["skeptic"] = "#4ECDC4",
        ["mediator"] = "#FF6B6B",
    };

    /// <summary>
    /// Session ID prefix for the philosophy module seed data node.
    /// </summary>
    public string PhilosophySessionId { get; set; } = "config-philosophy";

    /// <summary>
    /// Session ID prefix for the quality gate config seed data node.
    /// </summary>
    public string QualityGatesSessionId { get; set; } = "config-quality-gates";
}
