using SeldonLattice.Application.Agents;
using Xunit;

namespace SeldonLattice.Application.Tests;

/// <summary>
/// Unit tests for AgentResponseParser - the schema boundary that splits a raw LLM
/// turn into private "reasoning" and the clean spoken "response". Covers the JSON
/// path plus every fallback for the format leaks observed in production logs.
/// </summary>
public class AgentResponseParserTests
{
    [Fact]
    public void Parse_WellFormedJson_ReturnsResponseAndKeepsReasoningPrivate()
    {
        var raw = "{\"reasoning\": \"target the definition's counterexample\", \"response\": \"Courage is the Form of steadfastness.\"}";

        var result = AgentResponseParser.Parse(raw);

        Assert.Equal("Courage is the Form of steadfastness.", result.Content);
        Assert.Equal("target the definition's counterexample", result.Reasoning);
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsEmptyResponse()
    {
        Assert.Equal(string.Empty, AgentResponseParser.Parse(string.Empty).Content);
        Assert.Equal(string.Empty, AgentResponseParser.Parse(null!).Content);
        Assert.Equal(string.Empty, AgentResponseParser.Parse("   ").Content);
    }

    [Fact]
    public void Parse_PlainProse_TreatsWholeThingAsResponse()
    {
        var raw = "Courage is the mean between cowardice and rashness.";

        var result = AgentResponseParser.Parse(raw);

        Assert.Equal(raw, result.Content);
        Assert.Null(result.Reasoning);
    }

    [Fact]
    public void Parse_JsonInCodeFences_StripsFences()
    {
        var raw = "```json\n{\"reasoning\": \"notes\", \"response\": \"Courage requires discernment.\"}\n```";

        var result = AgentResponseParser.Parse(raw);

        Assert.Equal("Courage requires discernment.", result.Content);
    }

    [Fact]
    public void Parse_TwoSeparateJsonObjects_ExtractsSecondResponse()
    {
        // Leak pattern: model emits {"reasoning": ...} then {"response": ...} as two
        // back-to-back objects instead of one.
        var raw = "{\"reasoning\": \"I will refine my definition.\"}\n{\"response\": \"Courage must understand the nature of peril.\"}";

        var result = AgentResponseParser.Parse(raw);

        Assert.Equal("Courage must understand the nature of peril.", result.Content);
    }

    [Fact]
    public void Parse_MarkdownReasoningAndResponse_SplitsAndKeepsReasoningPrivate()
    {
        // Leak pattern: model emits **Reasoning**: / **Response**: labels instead of JSON.
        var raw = "**Reasoning**: The philosopher introduces emotional stability.\n\n**Response**: Your insight resonates deeply, for courage must embrace composure.";

        var result = AgentResponseParser.Parse(raw);

        Assert.Equal("Your insight resonates deeply, for courage must embrace composure.", result.Content);
        Assert.Equal("The philosopher introduces emotional stability.", result.Reasoning);
    }

    [Fact]
    public void Parse_MarkdownLabelWithColonAfterClosing_StripsStrayColon()
    {
        // Leak pattern: "**Label**:" leaves a stray ":" after sanitizing the bold text.
        var raw = "**Reasoning**: notes\n**Response**: Courage balances knowledge with emotional fortitude.";

        var result = AgentResponseParser.Parse(raw);

        Assert.Equal("Courage balances knowledge with emotional fortitude.", result.Content);
        Assert.DoesNotContain(":", result.Content);
    }

    [Fact]
    public void Parse_BracketedLabel_StrippedFromResponse()
    {
        var raw = "[DIALECTICAL INVERSION: FLIP THE ENTIRE FRAMEWORK]\nCourage is a matter of context.";

        var result = AgentResponseParser.Parse(raw);

        Assert.Equal("Courage is a matter of context.", result.Content);
    }

    [Fact]
    public void Parse_ReasoningThenSpokenText_ExtractsSpokenContent()
    {
        // Leak pattern: leading {"reasoning": ...} object followed by plain prose.
        var raw = "{\"reasoning\": \"line up a counterexample\"}\n\"The steady mind endures what must be endured.\"";

        var result = AgentResponseParser.Parse(raw);

        Assert.Equal("The steady mind endures what must be endured.", result.Content);
        Assert.Equal("line up a counterexample", result.Reasoning);
    }

    [Fact]
    public void Parse_OrphanedBoldDelimiter_StrippedFromResponse()
    {
        // Leak pattern: bare "**" on its own line at the start of the response.
        var raw = "**\n\nYour scenario of the military strategist highlights well the intricacies.";

        var result = AgentResponseParser.Parse(raw);

        Assert.Equal("Your scenario of the military strategist highlights well the intricacies.", result.Content);
        Assert.DoesNotContain("**", result.Content);
    }

    [Fact]
    public void Parse_JsonWithUnescapedNewlines_ExtractsResponse()
    {
        // Small models emit JSON with literal newlines inside string values instead
        // of \n escape sequences, which breaks JsonDocument.Parse.
        var raw = "{\"reasoning\": \"line up a counterexample\", \"response\": \"Line one.\nLine two.\nLine three.\"}";

        var result = AgentResponseParser.Parse(raw);

        Assert.Contains("Line one.", result.Content);
        Assert.Contains("Line two.", result.Content);
        Assert.Contains("Line three.", result.Content);
        Assert.Equal("line up a counterexample", result.Reasoning);
    }

    [Fact]
    public void Parse_LabeledSections_ExtractsReasoningAndResponse()
    {
        // Primary format: [REASONING] / [RESPONSE] section markers.
        var raw = """
            [REASONING]
            I will propose a counterexample about the soldier.

            [RESPONSE]
            Consider a soldier who faces the enemy with a calm and steady mind.
            """; 

        var result = AgentResponseParser.Parse(raw);

        Assert.Equal("Consider a soldier who faces the enemy with a calm and steady mind.", result.Content);
        Assert.Equal("I will propose a counterexample about the soldier.", result.Reasoning);
    }

    [Fact]
    public void Parse_LabeledSections_ResponseOnly_ExtractsResponse()
    {
        // Model omits reasoning section, only provides [RESPONSE].
        var raw = """
            [RESPONSE]
            Courage is the mean between cowardice and rashness.
            """; 

        var result = AgentResponseParser.Parse(raw);

        Assert.Equal("Courage is the mean between cowardice and rashness.", result.Content);
        Assert.Null(result.Reasoning);
    }

    [Fact]
    public void Parse_LeadingColonAfterResponseMarker_StripsColon()
    {
        // Some models emit ": " after [RESPONSE] before the actual content.
        var raw = """
            [REASONING]
            targeting the distinction
            
            [RESPONSE]
            : Courage is the mean between cowardice and rashness.
            """;

        var result = AgentResponseParser.Parse(raw);

        Assert.Equal("Courage is the mean between cowardice and rashness.", result.Content);
    }

    [Fact]
    public void Parse_JsonResponseWithLeadingColon_StripsColon()
    {
        var raw = "{\"reasoning\": \"...\", \"response\": \": The doctrine of stare decisis requires consistency.\"}";

        var result = AgentResponseParser.Parse(raw);

        Assert.Equal("The doctrine of stare decisis requires consistency.", result.Content);
    }

    [Fact]
    public void Parse_PlainTextWithLeadingColon_StripsColon()
    {
        var raw = ": Courage is the mean between cowardice and rashness.";

        var result = AgentResponseParser.Parse(raw);

        Assert.Equal("Courage is the mean between cowardice and rashness.", result.Content);
    }
}
