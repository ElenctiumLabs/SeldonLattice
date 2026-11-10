namespace SeldonLattice.Domain.Models.Storage;

/// <summary>
/// Lifecycle state of a dialectical session.
/// </summary>
public enum SessionStatus
{
    /// <summary>Visible in active lists and available for continuation.</summary>
    Active = 0,

    /// <summary>Retained for audit (nodes remain in the Merkle DAG) but hidden from active lists.</summary>
    Archived = 1,
}