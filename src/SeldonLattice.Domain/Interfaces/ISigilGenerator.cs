using SeldonLattice.Domain.Models.Results;

namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Service for generating visual sigil patterns from content-addressed identifiers (CIDs).
/// Each algorithm produces a deterministic, reproducible pattern from the SHA-256 hash
/// embedded in the CID, enabling cryptographic hash visualization.
/// </summary>
public interface ISigilGenerator
{
    /// <summary>
    /// Generates a sigil using the specified algorithm.
    /// </summary>
    Task<SigilResult> GenerateAsync(string cid, SigilAlgorithm algorithm, int size);

    /// <summary>
    /// Generates sigils using all available algorithms.
    /// </summary>
    Task<IEnumerable<SigilResult>> GenerateAllAsync(string cid, int size);
}
