using Microsoft.Extensions.Logging.Abstractions;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Infrastructure.Pqc;
using SeldonLattice.Infrastructure.Storage;
using SeldonLattice.Infrastructure.Verification;
using Xunit;

namespace SeldonLattice.Application.Tests;

public class SessionVerifierTests
{
    private const string SessionA = "session-a";
    private const string SessionB = "session-b";

    private static (SessionVerifier verifier, SignedMerkleStore signed, FasterMerkleStore raw, MLDsaSigner signer) CreateSystem()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "seldon-verify", Guid.NewGuid().ToString("N"));
        var raw = new FasterMerkleStore(baseDir);
        var signer = new MLDsaSigner(Path.Combine(baseDir, "keys"), NullLogger<MLDsaSigner>.Instance);
        var signed = new SignedMerkleStore(raw, signer, NullLogger<SignedMerkleStore>.Instance);
        var verifier = new SessionVerifier(signed, signer, NullLogger<SessionVerifier>.Instance);
        return (verifier, signed, raw, signer);
    }

    private static DialecticalNode Synthesis(string session, string content, string[] parents) => new()
    {
        NodeType = "socratic_synthesis",
        SessionId = session,
        Level = 1,
        Content = content,
        SpeakerId = "mediator",
        ParentHashes = parents,
        Timestamp = DateTime.UtcNow,
    };

    private static ArgumentClaimNode Claim(string session, string text, string synthesisCid, string[]? supportedBy = null, string[]? attacks = null) => new()
    {
        SessionId = session,
        Level = 1,
        ClaimText = text,
        Confidence = 0.8f,
        FalsifiedBy = "#data would refute this",
        SupportedBy = supportedBy ?? [synthesisCid],
        Attacks = attacks ?? [],
        ParentHashes = [synthesisCid],
        Timestamp = DateTime.UtcNow,
    };

    [Fact]
    public async Task VerifyIntactSession_Passes()
    {
        var (verifier, signed, _, _) = CreateSystem();

        var questionCid = await signed.PutBlobAsync(new DialecticalNode
        {
            NodeType = "question", SessionId = SessionA, Level = 0,
            Content = "what is justice?", Timestamp = DateTime.UtcNow,
        });
        var synthesisCid = await signed.PutBlobAsync(Synthesis(SessionA, "balancing competing freedoms", [questionCid.ToString()]));
        await signed.PutBlobAsync(Claim(SessionA, "justice requires balancing freedoms", synthesisCid.ToString()));
        await signed.PutBlobAsync(new LlmAuditNode
        {
            SessionId = SessionA, SpeakerId = "mediator", CompletionText = "ok",
            ParentCid = synthesisCid.ToString(), Timestamp = DateTime.UtcNow,
        });

        var report = await verifier.VerifySessionAsync(SessionA);

        Assert.True(report.Passed);
        Assert.Equal(4, report.NodesChecked);
        Assert.Equal(4, report.SignaturesVerified);
        Assert.DoesNotContain(report.Issues, i => i.Severity == VerificationSeverity.Error);
    }

    [Fact]
    public async Task VerifyDanglingReference_ReportsError()
    {
        var (verifier, signed, _, _) = CreateSystem();

        var synthesisCid = await signed.PutBlobAsync(Synthesis(SessionA, "a synthesis with no parents", []));
        // SupportedBy references a CID that was never stored.
        await signed.PutBlobAsync(Claim(SessionA, "a claim citing a phantom node",
            synthesisCid.ToString(),
            supportedBy: ["QmBogus00000000000000000000000000000000000000000000000"]));

        var report = await verifier.VerifySessionAsync(SessionA);

        Assert.False(report.Passed);
        Assert.Equal(1, report.PointerIssues);
        Assert.Contains(report.Issues, i =>
            i.Severity == VerificationSeverity.Error && i.Message.Contains("Dangling reference", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyForgedSignature_ReportsTamperEvidence()
    {
        var (verifier, signed, raw, signer) = CreateSystem();

        var node = Synthesis(SessionA, "original truth", []);
        var cid = await signed.PutBlobAsync(node);
        await signed.PutBlobAsync(Claim(SessionA, "a released claim", cid.ToString()));

        // Simulate tamper: replace the companion signature of the synthesis with a
        // valid signature over different content.
        var forged = await signer.SignAsync("mediator", [.. "forged content"u8]);
        await raw.PutSignatureAsync(forged with { SignedCid = cid.ToString() });

        var report = await verifier.VerifySessionAsync(SessionA);

        Assert.False(report.Passed);
        Assert.Equal(1, report.SignatureIssues);
        Assert.Contains(report.Issues, i =>
            i.Severity == VerificationSeverity.Error && i.Message.Contains("tamper evidence", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyMissingAuditSignature_ReportsError()
    {
        var (verifier, _, raw, _) = CreateSystem();

        // Audit node written through the raw store is deliberately left unsigned.
        await raw.PutBlobAsync(new LlmAuditNode
        {
            SessionId = SessionA, SpeakerId = "system", CompletionText = "raw",
            Timestamp = DateTime.UtcNow,
        });

        var report = await verifier.VerifySessionAsync(SessionA);

        Assert.False(report.Passed);
        Assert.Contains(report.Issues, i =>
            i.Severity == VerificationSeverity.Error && i.Message.Contains("Missing ML-DSA", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyUnsignedDialecticalNode_IsWarning_NotFatal()
    {
        var (verifier, _, raw, _) = CreateSystem();

        await raw.PutBlobAsync(new DialecticalNode
        {
            NodeType = "proposer_reply", SessionId = SessionA, Level = 1,
            Content = "legacy unsigned reply", SpeakerId = "proposer",
            Timestamp = DateTime.UtcNow,
        });

        var report = await verifier.VerifySessionAsync(SessionA);

        Assert.True(report.Passed);
        Assert.Contains(report.Issues, i =>
            i.Severity == VerificationSeverity.Warning && i.Message.Contains("Missing ML-DSA", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyOtherSessions_Ignored()
    {
        var (verifier, signed, _, _) = CreateSystem();

        // Session B has a daggy cyclic claim that must not leak into Session A's verdict.
        var bQuestion = await signed.PutBlobAsync(new DialecticalNode
        {
            NodeType = "question", SessionId = SessionB, Level = 0,
            Content = "other tree", Timestamp = DateTime.UtcNow,
        });
        await signed.PutBlobAsync(Claim(SessionB, "other session claim", bQuestion.ToString()));

        var report = await verifier.VerifySessionAsync(SessionA);

        Assert.True(report.Passed);
        Assert.Equal(0, report.NodesChecked);
        Assert.Contains(report.Issues, i =>
            i.Severity == VerificationSeverity.Warning && i.Message.Contains("No verifiable nodes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyRecomputedCid_MatchesStoreAddress_SurfacesCrossSessionDanglingClaim()
    {
        var (verifier, signed, _, _) = CreateSystem();

        // A claim in session A references a node in session B (legitimate cross-session
        // grounding). Its reference must still resolve, so the verdict remains green.
        var bSynthesis = await signed.PutBlobAsync(Synthesis(SessionB, "foundational prior synthesis", []));
        await signed.PutBlobAsync(Claim(SessionA, "leans on a prior session's synthesis", bSynthesis.ToString()));

        // A claim in session A dangling into the void must fail. Both of its link
        // kinds (ParentHashes and SupportedBy) cite the same phantom CID, so it may
        // surface as several issues - assert at least one pointer issue.
        await signed.PutBlobAsync(Claim(SessionA, "points at nothing", "SH3-NOWHERE"));

        var report = await verifier.VerifySessionAsync(SessionA);

        Assert.False(report.Passed);
        Assert.True(report.PointerIssues >= 1);
        Assert.Contains(report.Issues, i =>
            i.Severity == VerificationSeverity.Error && i.Message.Contains("SH3-NOWHERE", StringComparison.Ordinal));
    }
}