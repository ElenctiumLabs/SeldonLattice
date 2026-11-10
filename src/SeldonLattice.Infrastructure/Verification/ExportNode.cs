using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Infrastructure.Verification;

/// <summary>
/// One machine-verifiable node exported from a session. Carries the exact Merkle
/// serialized bytes the CID and ML-DSA companion signature were computed over,
/// so an offline consumer can re-derive every check the live `SessionVerifier`
/// enforces against the store.
/// </summary>
public sealed record ExportNode(
    string Kind,
    string Cid,
    int Level,
    string? SpeakerId,
    DateTime Timestamp,
    string BlobBase64,
    IReadOnlyList<string> Links,
    AgentSignature? Signature);