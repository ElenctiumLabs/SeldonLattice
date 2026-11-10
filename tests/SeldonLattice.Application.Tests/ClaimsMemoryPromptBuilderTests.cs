using SeldonLattice.Application.Memory;
using Xunit;

namespace SeldonLattice.Application.Tests;

public class ClaimsMemoryPromptBuilderTests
{
    private static RetrievedClaim Claim(string text, float confidence = 0.6f, int level = 2, string? falsifiedBy = null) =>
        new(
            Level: level,
            SessionId: "session-1",
            Claim: text,
            Confidence: confidence,
            Similarity: 0.9f,
            FalsifiedBy: falsifiedBy,
            SupportedBy: ["x"]);

    [Fact]
    public void Build_Null_WhenNoClaims()
    {
        Assert.Null(ClaimMemoryPromptBuilder.Build([], 5));
    }

    [Fact]
    public void Build_Null_WhenMaxClaimsIsZero()
    {
        Assert.Null(ClaimMemoryPromptBuilder.Build([Claim("any")], 0));
        Assert.Null(ClaimMemoryPromptBuilder.Build([Claim("any")], -1));
    }

    [Fact]
    public void Build_IncludesPrivateScaffoldingFraming()
    {
        var block = ClaimMemoryPromptBuilder.Build([Claim("the claim")], 5);
        Assert.NotNull(block);
        Assert.Contains("PRIOR ADJUDICATED POSITIONS", block);
        Assert.Contains("do not echo them", block);
    }

    [Fact]
    public void Build_ListsClaimsInGivenOrder()
    {
        var block = ClaimMemoryPromptBuilder.Build(
            [Claim("first claim", 0.6f, 1), Claim("second claim", 0.8f, 3)], 5);

        var firstIndex = block!.IndexOf("first claim", StringComparison.Ordinal);
        var secondIndex = block.IndexOf("second claim", StringComparison.Ordinal);
        Assert.True(firstIndex >= 0 && secondIndex > firstIndex, "claims must appear in order");
    }

    [Fact]
    public void Build_CapsNumberOfClaims()
    {
        var claims = Enumerable.Range(0, 10)
            .Select(i => Claim($"claim {i}"))
            .ToList();

        var block = ClaimMemoryPromptBuilder.Build(claims, 3);
        Assert.Contains("claim 0", block);
        Assert.DoesNotContain("claim 3", block);
        Assert.DoesNotContain("claim 9", block);
    }

    [Fact]
    public void Build_RendersConfidenceAsRoundedPercent()
    {
        var block = ClaimMemoryPromptBuilder.Build([Claim("a claim", 0.634f)], 5);
        Assert.Contains("confidence 63%", block);
    }

    [Fact]
    public void Build_TruncatesOverlongClaimText()
    {
        var longText = new string('x', ClaimMemoryPromptBuilder.MaxClaimCharacters + 200);
        var block = ClaimMemoryPromptBuilder.Build([Claim(longText)], 5);

        Assert.NotNull(block);
        Assert.Contains(new string('x', ClaimMemoryPromptBuilder.MaxClaimCharacters) + "…", block);
        Assert.DoesNotContain(new string('x', ClaimMemoryPromptBuilder.MaxClaimCharacters + 1), block);
    }

    [Fact]
    public void Build_MentionsFalsifiedCounterclaimWhenPresent()
    {
        var block = ClaimMemoryPromptBuilder.Build([Claim("the claim", falsifiedBy: "an earlier refutation")], 5);
        Assert.Contains("falsified by: an earlier refutation", block);
    }

    [Fact]
    public void Build_OmitsFalsificationSectionWhenAbsent()
    {
        var block = ClaimMemoryPromptBuilder.Build([Claim("the claim")], 5);
        Assert.DoesNotContain("falsified by:", block);
    }

    [Fact]
    public void Build_IsDeterministicForSameInput()
    {
        var claims = new[] { Claim("a", 0.5f, 1), Claim("b", 0.7f, 2) };
        var first = ClaimMemoryPromptBuilder.Build(claims, 5);
        var second = ClaimMemoryPromptBuilder.Build(claims, 5);
        Assert.Equal(first, second);
    }
}