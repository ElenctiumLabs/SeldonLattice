using Microsoft.Extensions.Logging;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Domain.Verification;

namespace SeldonLattice.Infrastructure.Verification;

/// <summary>
/// Verifies the cryptographic and structural integrity of a session's reasoning chain.
/// </summary>
public interface ISessionVerifier
{
    /// <summary>
    /// Verifies a session without checking it against an externally recorded root.
    /// </summary>
    Task<SessionVerificationReport> VerifySessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Verifies a session and, when <paramref name="expectedRoot"/> is supplied, checks the
    /// recomputed session root against it.
    /// </summary>
    Task<SessionVerificationReport> VerifySessionAsync(
        string sessionId, Cid? expectedRoot, CancellationToken ct = default);
}

/// <summary>
/// Verifies the integrity of a session's reasoning chain.
/// <para>
/// Every node in the session is checked four ways:
/// </para>
/// <list type="number">
/// <item>the identifier the node is filed under must still hash to its current bytes;</item>
/// <item>every cross-reference must resolve to a node that exists in the store;</item>
/// <item>a companion signature must exist (per policy), must be bound to this exact node,
/// must verify against the node's bytes, and must come from a pinned key when the policy
/// lists trusted signers;</item>
/// <item>when a root is supplied, the recomputed session root must match it.</item>
/// </list>
/// <para>
/// Verification is read-only and derives everything from stored bytes. It cannot establish
/// who produced a chain: signatures are self-verifying against the key they carry. A caller
/// that needs authenticity must pin the expected root and the accepted signer keys out of
/// band via <see cref="VerificationPolicy.TrustedSignerPublicKeys"/>.
/// </para>
/// </summary>
public sealed class SessionVerifier : ISessionVerifier
{
    private readonly IMerkleStore _store;
    private readonly IPqcSigner _signer;
    private readonly ILogger<SessionVerifier> _logger;
    private readonly VerificationPolicy _policy;

    /// <summary>
    /// Creates a verifier.
    /// </summary>
    /// <param name="store">Store to read.</param>
    /// <param name="signer">Signature implementation used to verify companions.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="policy">Strictness policy; defaults to <see cref="VerificationPolicy.Strict"/>.</param>
    public SessionVerifier(
        IMerkleStore store,
        IPqcSigner signer,
        ILogger<SessionVerifier> logger,
        VerificationPolicy? policy = null)
    {
        _store = store;
        _signer = signer;
        _logger = logger;
        _policy = policy ?? VerificationPolicy.Strict;
    }

    /// <inheritdoc />
    public Task<SessionVerificationReport> VerifySessionAsync(string sessionId, CancellationToken ct = default) =>
        VerifySessionAsync(sessionId, expectedRoot: null, ct);

    /// <inheritdoc />
    public async Task<SessionVerificationReport> VerifySessionAsync(
        string sessionId, Cid? expectedRoot, CancellationToken ct = default)
    {
        var issues = new List<VerificationIssue>();
        var nodesChecked = 0;
        var signaturesVerified = 0;
        var pointerIssues = 0;
        var signatureIssues = 0;
        var contentIssues = 0;
        var unreadableBlobs = 0;

        // Scan every type once. The scans are store-wide because cross-session references
        // (citations, reused syntheses) must resolve; the session filter is applied below.
        var dialectical = await _store.ScanBlobsAsync<DialecticalNode>(ct);
        var claims = await _store.ScanBlobsAsync<ArgumentClaimNode>(ct);
        var audits = await _store.ScanBlobsAsync<LlmAuditNode>(ct);

        // The universe of resolvable identifiers is what the store actually files, i.e. key CIDs.
        // A tampered node keeps its key, so links still resolve and the substitution is reported
        // once, on the node itself, rather than as dangling references from every child.
        var universe = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stored in dialectical.Nodes) universe.Add(stored.KeyCid.ToString());
        foreach (var stored in claims.Nodes) universe.Add(stored.KeyCid.ToString());
        foreach (var stored in audits.Nodes) universe.Add(stored.KeyCid.ToString());

        var sessionNodes = new List<(string Kind, Cid KeyCid, string[] Links, byte[] Bytes, Cid ContentCid)>();

        foreach (var stored in dialectical.Nodes.Where(s => s.Node.SessionId == sessionId))
        {
            sessionNodes.Add(("dialectical", stored.KeyCid,
                CollectDialecticalLinks(stored.Node), stored.Node.Serialize(), stored.Node.GetCid()));
        }

        foreach (var stored in claims.Nodes.Where(s => s.Node.SessionId == sessionId))
        {
            sessionNodes.Add(("claim", stored.KeyCid,
                CollectClaimLinks(stored.Node), stored.Node.Serialize(), stored.Node.GetCid()));
        }

        foreach (var stored in audits.Nodes.Where(s => s.Node.SessionId == sessionId))
        {
            sessionNodes.Add(("audit", stored.KeyCid,
                CollectAuditLinks(stored.Node), stored.Node.Serialize(), stored.Node.GetCid()));
        }

        foreach (var key in dialectical.UnreadableKeys
                     .Concat(claims.UnreadableKeys)
                     .Concat(audits.UnreadableKeys)
                     .Distinct(StringComparer.Ordinal))
        {
            unreadableBlobs++;
            issues.Add(new VerificationIssue(
                "store", key, VerificationSeverity.Error, "content.unreadable",
                $"Stored blob {key} could not be decoded; content is corrupt or no longer parseable."));
        }

        foreach (var (kind, keyCid, links, bytes, contentCid) in sessionNodes)
        {
            ct.ThrowIfCancellationRequested();
            nodesChecked++;

            var keyCidString = keyCid.ToString();

            // 1. Content addressing: the identifier the node is filed under must still describe
            //    its bytes. This is the substitution/modification check.
            if (contentCid.ToString() != keyCidString)
            {
                contentIssues++;
                issues.Add(new VerificationIssue(
                    kind, keyCidString, VerificationSeverity.Error, "content.id-mismatch",
                    $"Content hashes to {contentCid} but is filed under {keyCidString} - " +
                    "the node was modified or substituted after it was written."));
            }

            // 2. Reference integrity: every cross-reference must resolve to a stored node.
            foreach (var link in links)
            {
                if (string.IsNullOrWhiteSpace(link))
                {
                    continue;
                }

                if (!universe.Contains(link))
                {
                    pointerIssues++;
                    issues.Add(new VerificationIssue(
                        kind, keyCidString, VerificationSeverity.Error, "link.dangling",
                        $"Dangling reference: {link} does not resolve to any stored node."));
                }
            }

            // 3. Signature integrity: present, valid for these bytes, and bound to this node.
            var signature = await _store.GetSignatureAsync(keyCid);
            if (signature is null)
            {
                // Only audit nodes are required to be signed (see SignedMerkleStore); session
                // content written through the raw/legacy path is a non-fatal warning.
                if (_policy.RequireSignatures && kind == "audit")
                {
                    signatureIssues++;
                    issues.Add(new VerificationIssue(
                        kind, keyCidString, VerificationSeverity.Error, "signature.missing",
                        "Missing ML-DSA signature; the session policy requires every audit node to be signed."));
                }
                else
                {
                    issues.Add(new VerificationIssue(
                        kind, keyCidString, VerificationSeverity.Warning, "signature.missing",
                        "Missing ML-DSA signature; the node is unsigned (legacy or raw store write)."));
                }

                continue;
            }

            signaturesVerified++;
            if (!await _signer.VerifyAsync(signature, bytes, ct))
            {
                signatureIssues++;
                issues.Add(new VerificationIssue(
                    kind, keyCidString, VerificationSeverity.Error, "signature.invalid",
                    $"ML-DSA signature by {signature.SignerId} failed to verify against content - " +
                    "tamper evidence."));
                continue;
            }

            var boundCid = Cid.FromString(signature.SignedCid);
            if (!string.Equals(boundCid.ToString(), keyCidString, StringComparison.Ordinal))
            {
                signatureIssues++;
                issues.Add(new VerificationIssue(
                    kind, keyCidString, VerificationSeverity.Error, "signature.bound-to-other-node",
                    $"Signature by {signature.SignerId} is bound to {boundCid} but is filed against " +
                    $"{keyCidString}."));
                continue;
            }

            // 4. Signer identity: when the caller pins the accepted keys, a chain re-signed with
            //    an attacker's key is a policy failure even though every signature is valid.
            if (_policy.TrustedSignerPublicKeys.Count > 0 &&
                !_policy.TrustedSignerPublicKeys.Contains(signature.PublicKey))
            {
                signatureIssues++;
                issues.Add(new VerificationIssue(
                    kind, keyCidString, VerificationSeverity.Error, "signature.untrusted-key",
                    $"Signature by {signature.SignerId} verifies, but its public key is not in the " +
                    "trusted signer set - the chain was re-signed by an unknown key."));
            }
        }

        if (nodesChecked == 0)
        {
            issues.Add(new VerificationIssue(
                "session", sessionId, VerificationSeverity.Warning, "session.empty",
                "No verifiable nodes found for this session."));
        }

        var root = SessionRoot.Compute(sessionNodes.Select(node => node.KeyCid.ToString()));
        bool? rootMatches = null;
        if (expectedRoot is not null)
        {
            rootMatches = string.Equals(root.ToString(), expectedRoot.ToString(), StringComparison.Ordinal);
            if (rootMatches == false)
            {
                contentIssues++;
                issues.Add(new VerificationIssue(
                    "session", sessionId, VerificationSeverity.Error, "root.mismatch",
                    $"Session root {root} does not match the anchored root {expectedRoot} - " +
                    "nodes were added, removed, or replaced."));
            }
        }

        _logger.LogInformation(
            "Verified {Nodes} nodes for session {SessionId}: {Errors} error issues, root {Root}.",
            nodesChecked, sessionId, issues.Count(i => i.Severity == VerificationSeverity.Error), root);

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
            UnreadableBlobs = unreadableBlobs,
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

    private static string[] CollectAuditLinks(LlmAuditNode node) =>
        string.IsNullOrEmpty(node.ParentCid) ? [] : [node.ParentCid];
}
