using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Application.Agents.Moderators;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Agents;
using SeldonLattice.Domain.Models.Cycle;
using SeldonLattice.Domain.Models.Results;
using SeldonLattice.Domain.Options;
using Xunit;

namespace SeldonLattice.Application.Tests.Moderators;

/// <summary>
/// Regression tests for synthesis JSON parsing.
///
/// The motivating failure (2026-10-11): 8 of 10 syntheses in one session
/// were the hardcoded FallbackResult. The model returns TWO concatenated top-level JSON
/// objects - the five synthesis fields, then a second object carrying "claims" and
/// "remaining_dissensus" - because the grounded-synthesis mandate is presented as a separate
/// block. JsonDocument.Parse requires exactly one value and throws "Extra data" on the
/// second, and the parser's bare catch turned that into canned text.
///
/// These tests pin: concatenated objects merge; a single object still works; the primary
/// object wins on key conflict; genuinely malformed input still falls back.
/// </summary>
public class SynthesisJsonParsingTests
{
    /// <summary>Minimal moderator that exposes the protected parser.</summary>
    private sealed class TestModerator(Kernel kernel)
        : ModeratorBase(kernel, Options.Create(new DialecticsOptions()), new NoopRateLimiter())
    {
        protected override string FallbackImageDomain => "fallback-domain";

        protected override MetaphorResult FallbackResult => new(
            "FALLBACK METAPHOR", FallbackImageDomain, "FALLBACK THESIS",
            "FALLBACK NEXT", "FALLBACK CONNECTIVE");

        public override ChatHistory CreateSynthesisHistory(
            string theme, AgentResponse[] previousResponses,
            string moderationFocus, string cyclePhase = "growth")
            => [];

        protected override string BuildInterjectionPrompt(
            StalemateSignal signal, string theme, string d1Snippet, string d2Snippet,
            string d1Name, string d2Name) => string.Empty;

        /// <summary>Test hook into the protected parser.</summary>
        public MetaphorResult Parse(string raw) => ParseJsonSynthesisResponse(raw);

        /// <summary>
        /// Test hook into the merge helper, exposed so a failing merge reports its cause
        /// rather than being swallowed by the parser's catch.
        /// </summary>
        public static string MergeDiagnostic(string raw)
        {
            try
            {
                var el = ParseConcatenatedForTest(StripFences(raw));
                return $"OK keys=[{string.Join(",", el.EnumerateObject().Select(p => p.Name))}]";
            }
            catch (Exception ex)
            {
                return $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        private static string StripFences(string s)
        {
            s = s.Trim();
            s = System.Text.RegularExpressions.Regex.Replace(s, @"^\s*```(?:json)?\s*", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s*```\s*$", "");
            return s;
        }
    }

    private sealed class NoopRateLimiter : IProviderRateLimiter
    {
        public Task WaitForSlotAsync(CancellationToken ct = default) => Task.CompletedTask;
        public bool IsAvailable() => true;
        public void RecordRequest() { }
    }

    private static TestModerator Build() => new(Kernel.CreateBuilder().Build());

    [Fact]
    public void MergeDiagnostic_ReportsWhy()
    {
        const string raw = """
            {"image_domain": "Geological Structure", "metaphor": "M", "thesis": "T", "next_theme": "N", "connective_sentence": "C"}
            {"claims": [{"claim": "c", "support_from": ["s"], "attacked_by": ["a"], "falsified_by": "f", "confidence": 0.9}]}
            """;
        var diag = TestModerator.MergeDiagnostic(raw);
        Assert.StartsWith("OK", diag);
        Assert.Contains("image_domain", diag);
        Assert.Contains("claims", diag);
    }

    [Fact]
    public void OrphanedTail_ClosedEarlyThenMoreKeys_IsRecovered()
    {
        // The exact production shape from the 2026-10-11 incident: the first
        // object closes after the five synthesis fields, then the claims block follows with
        // NO opening brace. The model merged the contract's separate claims block into its
        // reply but forgot the second brace.
        const string raw = """
            {"image_domain": "architecture/structure", "metaphor": "Authentic dwelling is not escaping the black iron prison of Gestell, but recognizing the structure of the enclosure itself.", "thesis": "Authentic dwelling is achieved by attending to the structure of technological enframing rather than fleeing from it.", "next_theme": "The ethics of revealing", "connective_sentence": "This inquiry extends the question of authentic dwelling into ethical demands."}
            "claims": [
              {
                "claim": "Authentic dwelling is found in recognizing technological structure.",
                "support_from": ["the debate"],
                "attacked_by": ["the critique that acknowledging structure reinforces Gestell"],
                "falsified_by": "A sensor malfunction forcing unmediated confrontation with contingency.",
                "confidence": 0.95
              }
            ],
            "remaining_dissensus": "The boundary between radical presence and performance remains unstable.",
            "self_check": {
              "repeats_premise": 0.1,
              "grounded": true
            }
            """;

        var result = Build().Parse(raw);

        Assert.Equal("architecture/structure", result.ImageDomain);
        Assert.Contains("black iron prison", result.Metaphor);
        Assert.Equal("The ethics of revealing", result.NextTheme);
        Assert.NotNull(result.Claims);
        Assert.Single(result.Claims!);
        Assert.Equal("Authentic dwelling is found in recognizing technological structure.",
            result.Claims![0].Claim);
    }

    [Fact]
    public void OrphanedTail_IsDetectedByDiagnostic()
    {
        const string raw = """
            {"image_domain": "x", "metaphor": "M", "thesis": "T", "next_theme": "N", "connective_sentence": "C"}
            "claims": [{"claim": "c", "support_from": ["s"], "attacked_by": ["a"], "falsified_by": "f", "confidence": 0.9}]
            """;
        var diag = TestModerator.MergeDiagnostic(raw);
        Assert.StartsWith("OK", diag);
        Assert.Contains("claims", diag);
    }

    [Fact]
    public void OrphanedTail_WithNonAsciiCharacters_IsRecovered()
    {
        // The REAL production payload contains em-dashes and curly quotes. The first attempt
        // at this fix sliced the string using a BYTE offset (Utf8JsonReader.BytesConsumed)
        // against a CHAR string, which lands mid-character whenever non-ASCII text is present.
        // ASCII-only tests passed while every live synthesis still fell back.
        const string raw = "{\"image_domain\": \"architecture and excavation\", \"metaphor\": \"Authentic dwelling is not choosing a script within the simulation\u2019s display, but recognizing the latent possibility of Aletheia\u2014the unconcealment of Being\u2014by excavating the standing-reserve (Gestell) that frames all experience.\", \"thesis\": \"Authentic dwelling is the act of excavating genuine Being from the relentless framing of technological calculation.\", \"next_theme\": \"The ethical responsibility inherent in revealing unconcealment\", \"connective_sentence\": \"This inquiry generalizes the root question by shifting focus from the ontological ground to the ethical posture.\"}\n"
            + "\"claims\": [\n  {\n    \"claim\": \"Authentic dwelling is not choosing a script but excavating latent Being.\",\n    \"support_from\": [\"MEDIATOR SYNTHESIS \u2014 Seinsfrage as integration\"],\n    \"attacked_by\": [\"The dialecticians\u2019 argument that genuine action requires moving beyond display.\"],\n    \"falsified_by\": \"A concrete example of a technological system where the user disrupts its standard mode.\",\n    \"confidence\": 0.95\n  }\n],\n\"remaining_dissensus\": \"The precise threshold between necessary critique and paralyzing anxiety remains unresolved.\",\n\"self_check\": {\n  \"repeats_premise\": 0.1,\n  \"grounded\": true\n}";

        var result = Build().Parse(raw);

        Assert.Equal("architecture and excavation", result.ImageDomain);
        Assert.Contains("Aletheia", result.Metaphor);
        Assert.Equal("The ethical responsibility inherent in revealing unconcealment", result.NextTheme);
        Assert.NotNull(result.Claims);
        Assert.Single(result.Claims!);
    }

    //  The regression 

    [Fact]
    public void ConcatenatedObjects_AreMerged_NotRejected()
    {
        // The exact shape the model produced: synthesis fields, then claims in a second object.
        const string raw = """
            {"image_domain": "Geological Structure", "metaphor": "The subterranean network of Being.", "thesis": "Dwelling perceives the unseen root.", "next_theme": "The ethics of disclosure", "connective_sentence": "This leads to the ethics of disclosure."}
            {
              "claims": [
                {
                  "claim": "Authentic dwelling senses the subterranean connection.",
                  "support_from": ["dialecticians"],
                  "attacked_by": ["The assertion that observation suffices."],
                  "falsified_by": "A case showing surface reading is sufficient.",
                  "confidence": 0.95
                }
              ],
              "remaining_dissensus": "Whether structure is oppressive or a site of Being.",
              "self_check": { "repeats_premise": 0.1, "grounded": true }
            }
            """;

        var result = Build().Parse(raw);

        Assert.NotEqual("FALLBACK METAPHOR", result.Metaphor);
        Assert.Equal("Geological Structure", result.ImageDomain);
        Assert.Equal("The subterranean network of Being.", result.Metaphor);
        Assert.Equal("Dwelling perceives the unseen root.", result.Thesis);
        Assert.Equal("The ethics of disclosure", result.NextTheme);
        Assert.Equal("This leads to the ethics of disclosure.", result.ConnectiveSentence);
        // The second object's claims must survive the merge, not be discarded.
        Assert.NotNull(result.Claims);
        Assert.Single(result.Claims!);
    }

    [Fact]
    public void SingleObject_StillParses()
    {
        const string raw = """
            {"image_domain": "clearing", "metaphor": "M", "thesis": "T", "next_theme": "N", "connective_sentence": "C"}
            """;

        var result = Build().Parse(raw);

        Assert.Equal("clearing", result.ImageDomain);
        Assert.Equal("M", result.Metaphor);
        Assert.Equal("T", result.Thesis);
    }

    [Fact]
    public void PrimaryObject_WinsOnKeyConflict()
    {
        // First occurrence wins, so a later object cannot overwrite the synthesis fields.
        const string raw = """
            {"image_domain": "first", "metaphor": "FIRST", "thesis": "T1", "next_theme": "N1", "connective_sentence": "C1"}
            {"image_domain": "second", "metaphor": "SECOND", "thesis": "T2"}
            """;

        var result = Build().Parse(raw);

        Assert.Equal("first", result.ImageDomain);
        Assert.Equal("FIRST", result.Metaphor);
        Assert.Equal("T1", result.Thesis);
    }

    [Fact]
    public void ThreeConcatenatedObjects_AllMerge()
    {
        const string raw = """
            {"image_domain": "a", "metaphor": "M", "thesis": "T", "next_theme": "N", "connective_sentence": "C"}
            {"claims": [{"claim": "c1", "support_from": ["s"], "attacked_by": ["a"], "falsified_by": "f", "confidence": 0.9}]}
            {"remaining_dissensus": "still open"}
            """;

        var result = Build().Parse(raw);

        Assert.Equal("M", result.Metaphor);
        Assert.NotNull(result.Claims);
        Assert.Single(result.Claims!);
    }

    [Fact]
    public void CodeFencedConcatenatedObjects_Parse()
    {
        const string raw = """
            ```json
            {"image_domain": "clearing", "metaphor": "M", "thesis": "T", "next_theme": "N", "connective_sentence": "C"}
            {"claims": [{"claim": "c1", "support_from": ["s"], "attacked_by": ["a"], "falsified_by": "f", "confidence": 0.9}]}
            ```
            """;

        var result = Build().Parse(raw);

        Assert.Equal("M", result.Metaphor);
        Assert.NotNull(result.Claims);
    }

    [Fact]
    public void ProseInsteadOfJson_StillFallsBack()
    {
        // Genuinely unparseable output must still produce the fallback, not throw.
        var result = Build().Parse("I cannot answer that in JSON, but here is my thought.");

        Assert.Equal("FALLBACK METAPHOR", result.Metaphor);
        Assert.Equal("fallback-domain", result.ImageDomain);
    }

    [Fact]
    public void EmptyString_FallsBack()
    {
        var result = Build().Parse("");

        Assert.Equal("FALLBACK METAPHOR", result.Metaphor);
    }

    [Fact]
    public void MissingFields_UsePerFieldFallbacks()
    {
        // Object parses but lacks fields: image_domain falls back, metaphor/thesis fall back,
        // next_theme/connective become empty rather than throwing.
        var result = Build().Parse("""{"unrelated": true}""");

        Assert.Equal("fallback-domain", result.ImageDomain);
        Assert.Equal("FALLBACK METAPHOR", result.Metaphor);
        Assert.Equal("FALLBACK THESIS", result.Thesis);
        Assert.Equal(string.Empty, result.NextTheme);
    }

    [Fact]
    public void TrailingWhitespaceAndNewlines_AreTolerated()
    {
        const string raw = "\n\n   {\"image_domain\": \"x\", \"metaphor\": \"M\", \"thesis\": \"T\", \"next_theme\": \"N\", \"connective_sentence\": \"C\"}   \n\n";

        var result = Build().Parse(raw);

        Assert.Equal("M", result.Metaphor);
    }

    //  The negative-dialectic markers, on the shape the model ACTUALLY produced 

    [Fact]
    public void CodeFencedObject_WithNonEmptyMarkers_SurfacesThem()
    {
        // Verbatim shape from the LlmAuditNode of a real Qwen2.5-7B synthesis: the reply is a
        // ```json fenced object (not concatenated), and it carries the markers inside.
        const string raw = """
            ```json
            {
              "image_domain": "light and shadow",
              "metaphor": "In the depths of a cave, a single flame flickers.",
              "thesis": "The flame of meaning transforms shadows into guides.",
              "next_theme": "How does the integration of personal and collective suffering enhance societal resilience?",
              "connective_sentence": "By confronting our deepest fears, we heal.",
              "remaining_dissensus": "Whether integration is always preferable to repulsion.",
              "self_check": {
                "repeats_premise": 0.25
              }
            }
            ```
            """;

        var result = Build().Parse(raw);

        Assert.Equal("light and shadow", result.ImageDomain);
        Assert.Equal("Whether integration is always preferable to repulsion.", result.RemainingDissensus);
        Assert.Equal(0.25f, result.RepeatsPremise);
    }

    [Fact]
    public void CodeFencedObject_WithEmptyMarkers_YieldsNullMarkers()
    {
        // The REAL payload. The model emitted the fields but left them empty/zero, which the
        // contract permits ("empty only if the conflict is genuinely resolved"). An empty
        // remaining_dissensus must become null - not "" - so consumers can distinguish
        // "the model said nothing is unresolved" from "the model did not answer".
        // repeats_premise of 0.0 is a genuine VALUE and must survive as 0, not become null.
        const string raw = """
            ```json
            {
              "image_domain": "light and shadow",
              "metaphor": "In the depths of a cave, a single flame flickers.",
              "thesis": "The flame of meaning transforms shadows into guides.",
              "next_theme": "How does integration enhance resilience?",
              "connective_sentence": "By confronting our deepest fears, we heal.",
              "remaining_dissensus": "",
              "self_check": {
                "repeats_premise": 0.0
              }
            }
            ```
            """;

        var result = Build().Parse(raw);

        // Core fields parsed (so this is NOT the fallback path).
        Assert.NotEqual("FALLBACK METAPHOR", result.Metaphor);
        Assert.Equal("The flame of meaning transforms shadows into guides.", result.Thesis);

        // Empty string -> null, so "resolved" is distinguishable from "unanswered".
        Assert.Null(result.RemainingDissensus);

        // Zero is a real reading, and must be preserved.
        Assert.NotNull(result.RepeatsPremise);
        Assert.Equal(0.0f, result.RepeatsPremise!.Value);
    }

    [Fact]
    public void MarkersAbsentEntirely_YieldNullMarkers_WithoutFallingBack()
    {
        // A reply that simply omits the optional marker fields is still a valid synthesis.
        const string raw = """
            {"image_domain": "clearing", "metaphor": "M", "thesis": "T", "next_theme": "N", "connective_sentence": "C"}
            """;

        var result = Build().Parse(raw);

        Assert.Equal("M", result.Metaphor);
        Assert.Null(result.RemainingDissensus);
        Assert.Null(result.RepeatsPremise);
    }
}
