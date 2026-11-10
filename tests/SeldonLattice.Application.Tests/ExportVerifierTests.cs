using Microsoft.Extensions.Logging.Abstractions;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Infrastructure.Pqc;
using SeldonLattice.Infrastructure.Storage;
using SeldonLattice.Infrastructure.Verification;
using Xunit;

namespace SeldonLattice.Application.Tests;

public class ExportVerifierTests
{
    private const string SessionA = "session-export-a";

    private static (ExportVerifier verifier, SignedMerkleStore signed, FasterMerkleStore raw, MLDsaSigner signer) CreateSystem()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "seldon-export-verify", Guid.NewGuid().ToString("N"));
        var raw = new FasterMerkleStore(baseDir);
        var signer = new MLDsaSigner(Path.Combine(baseDir, "keys"), NullLogger<MLDsaSigner>.Instance);
        var signed = new SignedMerkleStore(raw, signer, NullLogger<SignedMerkleStore>.Instance);
        var verifier = new ExportVerifier(signed, signer, NullLogger<ExportVerifier>.Instance);
        return (verifier, signed, raw, signer);
    }

    private static DialecticalNode Synthesis(string content, string[] parents) => new()
    {
        NodeType = "socratic_synthesis", SessionId = SessionA, Level = 1,
        Content = content, SpeakerId = "mediator", ParentHashes = parents, Timestamp = DateTime.UtcNow,
    };

    private static ArgumentClaimNode Claim(string text, string synthesisCid, string[]? supportedBy = null) => new()
    {
        SessionId = SessionA, Level = 1, ClaimText = text, Confidence = 0.8f,
        FalsifiedBy = "#evidence would refute", SupportedBy = supportedBy ?? [synthesisCid],
        ParentHashes = [synthesisCid], Timestamp = DateTime.UtcNow,
    };

    private static LlmAuditNode Audit(string parentCid) => new()
    {
        SessionId = SessionA, SpeakerId = "mediator", CompletionText = "analysis",
        ParentCid = parentCid, Timestamp = DateTime.UtcNow,
    };

    [Fact]
    public async Task BuildAndVerify_SignedSession_Passes()
    {
        var (exportVerifier, signed, _, _) = CreateSystem();

        var q = await signed.PutBlobAsync(new DialecticalNode
        {
            NodeType = "question", SessionId = SessionA, Level = 0,
            Content = "what is justice?", Timestamp = DateTime.UtcNow,
        });
        var s = await signed.PutBlobAsync(Synthesis("balancing freedoms", [q.ToString()]));
        await signed.PutBlobAsync(Claim("justice balances", s.ToString()));
        await signed.PutBlobAsync(Audit(s.ToString()));

        var nodes = await exportVerifier.BuildExportNodesAsync(SessionA);
        var report = await exportVerifier.VerifyExportAsync(SessionA, nodes);

        Assert.True(report.Passed);
        Assert.Equal(4, report.NodesChecked);
        Assert.Equal(4, report.SignaturesVerified);
        Assert.DoesNotContain(report.Issues, i => i.Severity == VerificationSeverity.Error);
    }

    [Fact]
    public async Task VerifyTamperedBlob_ReportsError()
    {
        var (exportVerifier, signed, _, _) = CreateSystem();

        var q = await signed.PutBlobAsync(new DialecticalNode
        {
            NodeType = "question", SessionId = SessionA, Level = 0,
            Content = "original question", Timestamp = DateTime.UtcNow,
        });
        var nodes = (await exportVerifier.BuildExportNodesAsync(SessionA)).ToList();

        // Flip a byte in the blob to simulate tamper after export.
        var blob = Convert.FromBase64String(nodes[0].BlobBase64);
        blob[0] ^= 0xFF;
        var tampered = nodes[0] with { BlobBase64 = Convert.ToBase64String(blob) };

        var report = await exportVerifier.VerifyExportAsync(SessionA, [tampered]);

        Assert.False(report.Passed);
        Assert.Contains(report.Issues, i =>
            i.Severity == VerificationSeverity.Error && i.Message.Contains("does not match recomputed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyForgedSignature_ReportsTamperEvidence()
    {
        var (exportVerifier, signed, _, signer) = CreateSystem();

        var q = await signed.PutBlobAsync(new DialecticalNode
        {
            NodeType = "question", SessionId = SessionA, Level = 0,
            Content = "valid node", Timestamp = DateTime.UtcNow,
        });
        var nodes = (await exportVerifier.BuildExportNodesAsync(SessionA)).ToList();

        // Replace signature with one over different content.
        var blob = Convert.FromBase64String(nodes[0].BlobBase64);
        var forgedSig = await signer.SignAsync("intruder", [.. "completely different"u8]);
        var tampered = nodes[0] with { Signature = forgedSig };

        var report = await exportVerifier.VerifyExportAsync(SessionA, [tampered]);

        Assert.False(report.Passed);
        Assert.Contains(report.Issues, i =>
            i.Severity == VerificationSeverity.Error && i.Message.Contains("tamper evidence", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyMissingAuditSignature_ReportsError()
    {
        var (exportVerifier, signed, raw, _) = CreateSystem();

        var q = await signed.PutBlobAsync(new DialecticalNode
        {
            NodeType = "question", SessionId = SessionA, Level = 0,
            Content = "question", Timestamp = DateTime.UtcNow,
        });
        await signed.PutBlobAsync(Claim("a signed claim", q.ToString()));
        // Audit node written through the RAW store bypasses the signing decorator.
        await raw.PutBlobAsync(Audit(q.ToString()));

        var nodes = await exportVerifier.BuildExportNodesAsync(SessionA);

        var report = await exportVerifier.VerifyExportAsync(SessionA, nodes);

        Assert.False(report.Passed);
        Assert.Contains(report.Issues, i =>
            i.Severity == VerificationSeverity.Error && i.Message.Contains("Missing ML-DSA", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyDanglingLink_ReportsError()
    {
        var (exportVerifier, signed, _, _) = CreateSystem();

        var q = await signed.PutBlobAsync(new DialecticalNode
        {
            NodeType = "question", SessionId = SessionA, Level = 0,
            Content = "question", Timestamp = DateTime.UtcNow,
        });
        var nodes = (await exportVerifier.BuildExportNodesAsync(SessionA)).ToList();

        // Append a synthetic claim with a dangling SupportedBy not in the export.
        var fakeCid = "SH3-NONEXISTENT";
        var claim = Claim("a false claim", q.ToString(), [fakeCid]);
        var claimBlob = claim.Serialize();
        var claimNode = new ExportNode(
            Kind: "claim",
            Cid: claim.GetCid().ToString(),
            Level: 1, SpeakerId: null,
            Timestamp: claim.Timestamp,
            BlobBase64: Convert.ToBase64String(claimBlob),
            Links: [fakeCid],
            Signature: null);
        nodes = [.. nodes, claimNode];

        var report = await exportVerifier.VerifyExportAsync(SessionA, nodes);

        Assert.False(report.Passed);
        Assert.Contains(report.Issues, i =>
            i.Severity == VerificationSeverity.Error && i.Message.Contains("Dangling reference", StringComparison.Ordinal));
    }
}