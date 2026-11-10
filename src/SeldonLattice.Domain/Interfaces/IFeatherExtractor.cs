namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Extracts Ma'at feather invariants from the root question.
/// Implemented by an LLM-backed service; stubbed in tests.
/// </summary>
public interface IFeatherExtractor
{
    /// <summary>
    /// Extracts a configurable number of invariants from the root question.
    /// These invariants are frozen on the root DAG node and used to weigh claims.
    /// </summary>
    Task<IReadOnlyList<string>> ExtractInvariantsAsync(string rootQuestion, int count, CancellationToken ct = default);
}
