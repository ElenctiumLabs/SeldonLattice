using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Domain.Interfaces;

public interface ICitationProvider
{
    /// <summary>
    /// Return top-N citations relevant to the query. Confidence in [0,1].
    /// </summary>
    Task<IReadOnlyList<Citation>> GetTopCitationsAsync(string query, int topN = 5, CancellationToken ct = default);
}