namespace SeldonLattice.Domain.Models;

/// <summary>
/// A bias report produced by the audit engine after inspecting
/// the current distribution of ingested documents vs synthesis nodes.
/// </summary>
public record BiasReport
{
    /// <summary>Which cultural frames are over-represented.</summary>
    public Dictionary<string, int> CultureDistribution { get; init; } = [];
    
    /// <summary>Which domains are over-represented.</summary>
    public Dictionary<string, int> DomainDistribution { get; init; } = [];
    
    /// <summary>Which sources are under-represented and should be queried.</summary>
    public List<string> RecommendedSources { get; init; } = [];
    
    /// <summary>Suggested themes to challenge current biases.</summary>
    public List<string> SuggestedFalsificationThemes { get; init; } = [];
    
    /// <summary>Overall bias score 0.0 (balanced) to 1.0 (severely biased).</summary>
    public float BiasScore { get; init; }
    
    /// <summary>Human-readable summary of the bias assessment.</summary>
    public string Summary { get; init; } = string.Empty;
}