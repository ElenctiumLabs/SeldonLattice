using System.Text.Json;
using SeldonLattice.Application.Agents;
using Xunit;

namespace SeldonLattice.Application.Tests.Moderators;

/// <summary>
/// Guards the structured-output schema that constrains synthesis decoding.
///
/// Why these assertions matter: a 7B model drops the negative-dialectic markers from its
/// reply unless the decoder is constrained, and every stored synthesis then carries
/// remainingDissensus = null / repeatsPremise = 0.0 with nothing to distinguish "the model
/// had no dissensus to report" from "the model never emitted the field". The schema is what
/// makes those fields structurally present. Two ways it can silently stop doing that:
///
///   1. A marker key drops out of `required` - the grammar no longer forces it, and the
///      live failure returns with no test failing.
///   2. `claims` is omitted from `properties` - under strict enforcement the grammar then
///      forbids the key, and the grounded-synthesis claims array (S2 substrate) is stripped
///      from every reply. This was observed: the schema as first written dropped `claims`
///      from the model's output entirely.
/// </summary>
public class SynthesisSchemaTests
{
    private static JsonElement Schema()
    {
        var rf = SynthesisSchema.ResponseFormat;
        return rf.GetProperty("json_schema").GetProperty("schema");
    }

    [Fact]
    public void ResponseFormat_UsesJsonSchemaMode()
    {
        var rf = SynthesisSchema.ResponseFormat;

        Assert.Equal("json_schema", rf.GetProperty("type").GetString());
        Assert.True(rf.GetProperty("json_schema").GetProperty("strict").GetBoolean());
    }

    [Theory]
    // The five core synthesis fields plus the two negative-dialectic markers. If a marker
    // leaves this list the grammar stops enforcing it and the live regression returns.
    [InlineData("image_domain")]
    [InlineData("metaphor")]
    [InlineData("thesis")]
    [InlineData("next_theme")]
    [InlineData("connective_sentence")]
    [InlineData("remaining_dissensus")]
    [InlineData("self_check")]
    public void Schema_RequiresEverySynthesisField(string field)
    {
        var required = Schema()
            .GetProperty("required")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        Assert.Contains(field, required);
    }

    [Fact]
    public void Schema_AllowsTheClaimsArray()
    {
        // Strict enforcement permits only declared properties, so an undeclared `claims`
        // key is stripped from the reply rather than merely unvalidated.
        var properties = Schema().GetProperty("properties");

        Assert.True(properties.TryGetProperty("claims", out var claims),
            "claims must be declared or strict enforcement strips it from every synthesis");
        Assert.Equal("array", claims.GetProperty("type").GetString());
    }

    [Fact]
    public void Schema_RequiresTheMarkerSubfields()
    {
        var selfCheck = Schema()
            .GetProperty("properties")
            .GetProperty("self_check");

        var required = selfCheck
            .GetProperty("required")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        Assert.Contains("repeats_premise", required);
        Assert.Contains("grounded", required);
    }

    [Fact]
    public void ResponseFormat_IsReusableAcrossRequests()
    {
        // The element is built from a JsonDocument and cloned. An un-cloned element would
        // reference a disposed document's buffer and throw on second use.
        var first = SynthesisSchema.ResponseFormat;
        var second = SynthesisSchema.ResponseFormat;

        Assert.Equal(
            first.GetProperty("json_schema").GetProperty("schema").GetProperty("required").GetArrayLength(),
            second.GetProperty("json_schema").GetProperty("schema").GetProperty("required").GetArrayLength());
    }
}
