using System.Text;
using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Domain.Verification;

/// <summary>
/// Deterministic digest over the set of node identifiers that belong to a session.
/// <para>
/// A Merkle DAG is only tamper-evident relative to a root the attacker cannot rewrite.
/// The store alone is self-consistent: an attacker with write access can replace every
/// node, sign the replacements with their own key, and produce a chain that verifies
/// cleanly. Recording the session root somewhere outside the store (a commit reference,
/// a build artefact, an RFC 3161 timestamp, a transparency log) is what turns internal
/// consistency into evidence.
/// </para>
/// <para>
/// The root is order-independent and deduplicated, so it can be recomputed from any
/// complete copy of a session, in any traversal order, on any machine.
/// </para>
/// </summary>
public static class SessionRoot
{
    /// <summary>
    /// Domain separator. Changing it changes every root, so it is versioned.
    /// </summary>
    public const string Domain = "seldon-lattice/session-root/v1";

    /// <summary>
    /// Computes the session root over the supplied node identifiers.
    /// </summary>
    /// <param name="cids">Node identifiers, in any order.</param>
    /// <returns>The root identifier.</returns>
    public static Cid Compute(IEnumerable<string> cids)
    {
        ArgumentNullException.ThrowIfNull(cids);

        var canonical = cids
            .Where(cid => !string.IsNullOrWhiteSpace(cid))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(cid => cid, StringComparer.Ordinal);

        var payload = Domain + "\n" + string.Join("\n", canonical);
        return Cid.Compute(Encoding.UTF8.GetBytes(payload));
    }
}
