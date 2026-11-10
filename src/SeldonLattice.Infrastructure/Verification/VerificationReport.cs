using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Infrastructure.Verification;

/// <summary>Severity of a single verification finding.</summary>
public enum VerificationSeverity
{
    /// <summary>Fatal integrity failure (content substitution, dangling reference, failed signature).</summary>
    Error,

    /// <summary>Non-fatal observation (e.g. an unsigned legacy node, or an empty session).</summary>
    Warning
}

/// <summary>
/// A single finding from a verification run.
/// </summary>
/// <param name="NodeType">Node kind the finding applies to ("dialectical", "claim", "audit", "session").</param>
/// <param name="NodeCid">Identifier of the affected node, as filed in the store.</param>
/// <param name="Severity">Error or warning.</param>
/// <param name="Code">Stable machine-readable code, e.g. <c>content.id-mismatch</c>.</param>
/// <param name="Message">Human-readable explanation.</param>
public sealed record VerificationIssue(
    string NodeType,
    string NodeCid,
    VerificationSeverity Severity,
    string Code,
    string Message);

/// <summary>
/// Result of verifying a session's reasoning chain.
/// <see cref="Passed"/> is true only when there are no error-severity issues.
/// </summary>
public sealed record SessionVerificationReport(
    string SessionId,
    bool Passed,
    int NodesChecked,
    int SignaturesVerified,
    int PointerIssues,
    int SignatureIssues,
    IReadOnlyList<VerificationIssue> Issues)
{
    /// <summary>Nodes whose stored identifier did not match their own content.</summary>
    public int ContentIssues { get; init; }

    /// <summary>Stored keys whose payload could not be decoded.</summary>
    public int UnreadableBlobs { get; init; }

    /// <summary>Deterministic root over the verified node identifiers.</summary>
    public Cid? SessionRoot { get; init; }

    /// <summary>
    /// Whether <see cref="SessionRoot"/> matched the root supplied to the verifier.
    /// Null when no root was supplied.
    /// </summary>
    public bool? RootMatches { get; init; }

    /// <summary>True when the report contains a finding with the given code.</summary>
    public bool HasCode(string code) =>
        Issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal));

    /// <summary>All issues at error severity.</summary>
    public IEnumerable<VerificationIssue> Errors =>
        Issues.Where(issue => issue.Severity == VerificationSeverity.Error);
}
