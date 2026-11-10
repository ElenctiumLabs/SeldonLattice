using Microsoft.Extensions.Logging;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Domain.Verification;

namespace SeldonLattice.Infrastructure.Verification;

/// <summary>
/// Builds self-verifying machine-readable node sets from the live store, and verifies them
/// offline. Each exported node carries its original Merkle-serialized bytes and companion
/// signature, so the verifier re-derives every check from those bytes alone - no store
/// access is required.
/// </summary>
public sealed class ExportVerifier
{
    private readonly IMerkleStore _store;
    private readonly IPqcSigner _signer;
    private readonly ILogger<ExportVerifier> _logger;
    private readonly VerificationPolicy _policy;

    /// <summary>
    /// Creates an exporter/verifier pair.
    /// </summary>
    /// <param name="store">Store to export from.</param>
    /// <param name="signer">Signature implementation used to verify companions.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="policy">Strictness policy; defaults to <see cref="VerificationPolicy.Strict"/>.</param>
    public ExportVerifier(
        IMerkleStore store,
        IPqcSigner signer,
        ILogger<ExportVerifier> logger,
        VerificationPolicy? policy = null)
    {
        _store = store;
        _signer = signer;
        _logger = logger;
        _policy = policy ?? VerificationPolicy.Strict;
    }

    /// <summary>
    /// Produces the export-time list of machine-verifiable <see cref="ExportNode"/> records for a
    /// session, ordered deterministically by identifier so two exports of the same session are
    /// byte-identical.
    /// </summary>
    public async Task<IReadOnlyList<ExportNode>> BuildExportNodesAsync(
        string sessionId, CancellationToken ct = default)
    {
        var nodes = new List<ExportNode>();

        var dialectical = await _store.ScanBlobsAsync<DialecticalNode>(ct);
        var claims = await _store.ScanBlobsAsync<ArgumentClaimNode>(ct);
        var audits = await _store.ScanBlobsAsync<LlmAuditNode>(ct);

        foreach (var stored in dialectical.Nodes.Where(s => s.Node.SessionId == sessionId))
        {
            ct.ThrowIfCancellationRequested();
            var node = stored.Node;
            nodes.Add(new ExportNode(
                Kind: "dialectical",
                Cid: stored.KeyCid.ToString(),
                Level: node.Level,
                SpeakerId: node.SpeakerId,
                Timestamp: node.Timestamp,
                BlobBase64: Convert.ToBase64String(node.Serialize()),
                Links: CollectDialecticalLinks(node),
                Signature: await _store.GetSignatureAsync(stored.KeyCid)));
        }

        foreach (var stored in claims.Nodes.Where(s => s.Node.SessionId == sessionId))
        {
            ct.ThrowIfCancellationRequested();
            var node = stored.Node;
            nodes.Add(new ExportNode(
                Kind: "claim",
                Cid: stored.KeyCid.ToString(),
                Level: node.Level,
                SpeakerId: null,
                Timestamp: node.Timestamp,
                BlobBase64: Convert.ToBase64String(node.Serialize()),
                Links: CollectClaimLinks(node),
                Signature: await _store.GetSignatureAsync(stored.KeyCid)));
        }

        foreach (var stored in audits.Nodes.Where(s => s.Node.SessionId == sessionId))
        {
            ct.ThrowIfCancellationRequested();
            var node = stored.Node;
            nodes.Add(new ExportNode(
                Kind: "audit",
                Cid: stored.KeyCid.ToString(),
                Level: 0,
                SpeakerId: node.SpeakerId,
                Timestamp: node.Timestamp,
                BlobBase64: Convert.ToBase64String(node.Serialize()),
                Links: string.IsNullOrEmpty(node.ParentCid) ? [] : [node.ParentCid],
                Signature: await _store.GetSignatureAsync(stored.KeyCid)));
        }

        return [.. nodes.OrderBy(node => node.Cid, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Verifies a self-contained exported node set offline. No store access is needed; all
    /// checks are re-derived from the embedded bytes, identifiers, links, and signatures.
    /// </summary>
    /// <param name="sessionId">Session identifier, echoed in the report.</param>
    /// <param name="nodes">Exported nodes.</param>
    /// <param name="expectedRoot">Optional externally recorded session root to compare against.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<SessionVerificationReport> VerifyExportAsync(
        string sessionId,
        IReadOnlyList<ExportNode> nodes,
        Cid? expectedRoot = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var issues = new List<VerificationIssue>();
        var universe = new HashSet<string>(nodes.Select(node => node.Cid), StringComparer.Ordinal);

        var nodesChecked = 0;
        var signaturesVerified = 0;
        var pointerIssues = 0;
        var signatureIssues = 0;
        var contentIssues = 0;

        foreach (var node in nodes)
        {
            ct.ThrowIfCancellationRequested();
            nodesChecked++;

            // 1. Content addressing: recompute from the embedded bytes and assert the match.
            var blob = Convert.FromBase64String(node.BlobBase64);
            var recomputedCid = Cid.Compute(blob);
            if (!string.Equals(recomputedCid.ToString(), node.Cid, StringComparison.Ordinal))
            {
                contentIssues++;
                issues.Add(new VerificationIssue(
                    node.Kind, node.Cid, VerificationSeverity.Error, "content.id-mismatch",
                    $"Exported CID {node.Cid} does not match recomputed {recomputedCid} - tamper evidence."));
            }

            // 2. Link resolution: every link must resolve within the export.
            foreach (var link in node.Links.Where(link => !string.IsNullOrWhiteSpace(link)))
            {
                if (!universe.Contains(link))
                {
                    pointerIssues++;
                    issues.Add(new VerificationIssue(
                        node.Kind, node.Cid, VerificationSeverity.Error, "link.dangling",
                        $"Dangling reference: {link} does not resolve to any node in the export."));
                }
            }

            // 3. Signature integrity: present, valid for these bytes, and bound to this node.
            if (node.Signature is null)
            {
                // Only audit nodes are required to be signed (see SignedMerkleStore); session
                // content written through the raw/legacy path is a non-fatal warning.
                if (_policy.RequireSignatures && node.Kind == "audit")
                {
                    signatureIssues++;
                    issues.Add(new VerificationIssue(
                        node.Kind, node.Cid, VerificationSeverity.Error, "signature.missing",
                        "Missing ML-DSA signature; the session policy requires every audit node to be signed."));
                }
                else
                {
                    issues.Add(new VerificationIssue(
                        node.Kind, node.Cid, VerificationSeverity.Warning, "signature.missing",
                        "Missing ML-DSA signature; the node is unsigned (legacy or raw store write)."));
                }

                continue;
            }

            signaturesVerified++;
            if (!await _signer.VerifyAsync(node.Signature, blob, ct))
            {
                signatureIssues++;
                issues.Add(new VerificationIssue(
                    node.Kind, node.Cid, VerificationSeverity.Error, "signature.invalid",
                    $"ML-DSA signature by {node.Signature.SignerId} failed to verify against content - " +
                    "tamper evidence."));
                continue;
            }

            var boundCid = Cid.FromString(node.Signature.SignedCid);
            if (!string.Equals(boundCid.ToString(), node.Cid, StringComparison.Ordinal))
            {
                signatureIssues++;
                issues.Add(new VerificationIssue(
                    node.Kind, node.Cid, VerificationSeverity.Error, "signature.bound-to-other-node",
                    $"Signature by {node.Signature.SignerId} is bound to {boundCid} but accompanies " +
                    $"{node.Cid}."));
                continue;
            }

            // 4. Signer identity: a valid signature from an unpinned key is still a policy failure.
            if (_policy.TrustedSignerPublicKeys.Count > 0 &&
                !_policy.TrustedSignerPublicKeys.Contains(node.Signature.PublicKey))
            {
                signatureIssues++;
                issues.Add(new VerificationIssue(
                    node.Kind, node.Cid, VerificationSeverity.Error, "signature.untrusted-key",
                    $"Signature by {node.Signature.SignerId} verifies, but its public key is not in the " +
                    "trusted signer set - the export was re-signed by an unknown key."));
            }
        }

        if (nodesChecked == 0)
        {
            issues.Add(new VerificationIssue(
                "session", sessionId, VerificationSeverity.Warning, "session.empty",
                "No verifiable nodes found in the export."));
        }

        var root = SessionRoot.Compute(nodes.Select(node => node.Cid));
        bool? rootMatches = null;
        if (expectedRoot is not null)
        {
            rootMatches = string.Equals(root.ToString(), expectedRoot.ToString(), StringComparison.Ordinal);
            if (rootMatches == false)
            {
                contentIssues++;
                issues.Add(new VerificationIssue(
                    "session", sessionId, VerificationSeverity.Error, "root.mismatch",
                    $"Exported root {root} does not match the anchored root {expectedRoot} - " +
                    "nodes were added, removed, or replaced."));
            }
        }

        _logger.LogInformation(
            "Export verification for {SessionId}: {Nodes} nodes, {Errors} error issues, root {Root}.",
            sessionId, nodesChecked, issues.Count(i => i.Severity == VerificationSeverity.Error), root);

        return new SessionVerificationReport(
            sessionId,
            Passed: issues.All(issue => issue.Severity != VerificationSeverity.Error),
            nodesChecked,
            signaturesVerified,
            pointerIssues,
            signatureIssues,
            issues)
        {
            ContentIssues = contentIssues,
            SessionRoot = root,
            RootMatches = rootMatches,
        };
    }

    private static string[] CollectDialecticalLinks(DialecticalNode node)
    {
        var links = new List<string>((node.ParentHashes?.Length ?? 0) + 2)
        {
            node.ParentHash ?? string.Empty,
            node.ParentPatternHash ?? string.Empty
        };
        if (node.ParentHashes is { Length: > 0 })
        {
            links.AddRange(node.ParentHashes);
        }

        return [.. links];
    }

    private static string[] CollectClaimLinks(ArgumentClaimNode node)
    {
        var links = new List<string>();
        if (node.ParentHashes is { Length: > 0 })
        {
            links.AddRange(node.ParentHashes);
        }

        if (node.SupportedBy is { Length: > 0 })
        {
            links.AddRange(node.SupportedBy);
        }

        if (node.Attacks is { Length: > 0 })
        {
            links.AddRange(node.Attacks);
        }

        return [.. links];
    }
}
