namespace SeldonLattice.Domain.Models;

/// <summary>
/// Quality metrics snapshot for a single agent response.
/// </summary>
public record ResponseQualityMetrics
{
    /// <summary>Weighted ceremony language score.</summary>
    public double CeremonyScore { get; init; }

    /// <summary>Text similarity to previous response (0.0-1.0).</summary>
    public double SimilarityToPrevious { get; init; }

    /// <summary>Count of new arguments/counterexamples/questions.</summary>
    public int NewArgumentCount { get; init; }

    /// <summary>Overall quality score (0-100).</summary>
    public double QualityScore { get; init; }

    /// <summary>Word count of the response.</summary>
    public int WordCount { get; init; }

    /// <summary>Whether the response contains farewell language.</summary>
    public bool HasFarewell { get; init; }

    /// <summary>Whether the response contains agreement markers.</summary>
    public bool HasAgreement { get; init; }

    /// <summary>Whether the response contains disagreement indicators.</summary>
    public bool HasDisagreement { get; init; }

    /// <summary>Text similarity to the agent's own previous response (0.0-1.0).</summary>
    public double SelfSimilarity { get; init; }
}