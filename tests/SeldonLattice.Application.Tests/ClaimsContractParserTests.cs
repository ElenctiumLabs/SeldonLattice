using SeldonLattice.Application.Audit;
using Xunit;

namespace SeldonLattice.Application.Tests;

public class ClaimsContractParserTests
{
    [Fact]
    public void ParsesFullContractJson()
    {
        var json = """
            {
              "synthesisThesis": "thesis",
              "claims": [
                {
                  "claim": "Monometallism fails because silver has no single stable value",
                  "support_from": ["turn 3", "kimberley-2017"],
                  "attacked_by": ["free coinage gives elasticity"],
                  "falsified_by": "1896 US election returns in a silver-permitting district",
                  "confidence": 0.9
                }
              ]
            }
            """;

        Assert.True(ClaimsContractParser.TryParseClaims(json, out var claims));
        var c = Assert.Single(claims);
        Assert.Equal("Monometallism fails because silver has no single stable value", c.Claim);
        Assert.Equal(["turn 3", "kimberley-2017"], c.SupportedBy);
        Assert.Equal(["free coinage gives elasticity"], c.AttackedBy);
        Assert.Equal("1896 US election returns in a silver-permitting district", c.FalsifiedBy);
        Assert.Equal(0.9f, c.Confidence, 3);
    }

    [Fact]
    public void ToleratesExtraKeysAndStringConfidence()
    {
        var json = """
            {
              "claims": [
                {
                  "unexpected_key": "ignored",
                  "claim": "A",
                  "confidence": "0.65"
                },
                {
                  "claim": "B"
                }
              ]
            }
            """;

        Assert.True(ClaimsContractParser.TryParseClaims(json, out var claims));
        Assert.Equal(2, claims.Count);
        Assert.Equal(0.65f, claims[0].Confidence, 3);
        Assert.Equal((float)ClaimsContractParser.DefaultConfidence, claims[1].Confidence, 3);
        Assert.Empty(claims[1].SupportedBy);
    }

    [Fact]
    public void DropsMalformedClaimsButKeepsValidOnes()
    {
        var json = """
            { "claims": [
              { "claim": "valid claim", "confidence": 0.8 },
              { "falsified_by": "no claim text -> dropped" },
              { "claim": 123 }
            ] }
            """;

        Assert.True(ClaimsContractParser.TryParseClaims(json, out var claims));
        var c = Assert.Single(claims);
        Assert.Equal("valid claim", c.Claim);
    }

    [Fact]
    public void HandlesMarkdownFencedJson()
    {
        var json = """
            ```json
            { "claims": [ { "claim": "fenced but parsed", "confidence": 0.5 } ] }
            ```
            """;

        Assert.True(ClaimsContractParser.TryParseClaims(json, out var claims));
        Assert.Equal("fenced but parsed", Assert.Single(claims).Claim);
    }

    [Fact]
    public void AcceptsBareArrayRoot()
    {
        var json = """[ { "claim": "array root claim" } ]""";

        Assert.True(ClaimsContractParser.TryParseClaims(json, out var claims));
        Assert.Equal("array root claim", Assert.Single(claims).Claim);
    }

    [Fact]
    public void RejectsAbsentOrEmptyClaims()
    {
        Assert.False(ClaimsContractParser.TryParseClaims(@"{ ""thesis"": ""no claims"" }", out var a));
        Assert.False(ClaimsContractParser.TryParseClaims(@"{ ""claims"": [] }", out var b));
        Assert.False(ClaimsContractParser.TryParseClaims("not json at all", out var c));
        Assert.False(ClaimsContractParser.TryParseClaims("", out var d));
    }

    [Fact]
    public void ClampsConfidenceOutOfRange()
    {
        var json = """{ "claims": [ { "claim": "c", "confidence": 5.0 }, { "claim": "d", "confidence": -1.0 } ] }""";

        Assert.True(ClaimsContractParser.TryParseClaims(json, out var claims));
        Assert.Equal(2, claims.Count);
        Assert.Equal(1f, claims[0].Confidence);
        Assert.Equal(0f, claims[1].Confidence);
    }
}