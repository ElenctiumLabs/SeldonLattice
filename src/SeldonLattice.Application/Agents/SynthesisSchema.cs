using System.Text.Json;

namespace SeldonLattice.Application.Agents;

/// <summary>
/// The structured-output schema for a synthesis reply, sent as OpenAI-style
/// <c>response_format: {type: "json_schema"}</c>.
///
/// Why this exists: the synthesis prompt already *asks* for the negative-dialectic markers
/// (<c>remaining_dissensus</c>, <c>self_check.repeats_premise</c>), but a 7B local model
/// silently drops them - measured on Qwen3-30B-A3B-Instruct-2507, the reply contained the five
/// core fields and neither marker key, so every stored synthesis carried
/// <c>remainingDissensus = null</c> and <c>repeatsPremise = 0.0</c> and the Step 4
/// experiment had no live signal to measure.
///
/// Asking harder is not a fix: instruction-following degrades with prompt length and varies
/// per model. Constraining the decoder is: with the schema set, llama.cpp applies a GBNF
/// grammar derived from it, so the marker keys cannot be omitted regardless of what the
/// prompt says or which model is loaded. Verified against the live server - a prompt that
/// never mentions the marker fields still returned all seven, because the grammar, not the
/// instruction, is what enforces presence.
///
/// Scope: the schema constrains *presence and shape*. The prompt still governs the *values*
/// (a model told nothing about <c>repeats_premise</c> emits a meaningless 0). The two are
/// complementary - see <see cref="PromptContracts.ReasonableSynthesisContract"/>.
/// </summary>
public static class SynthesisSchema
{
    /// <summary>
    /// The JSON Schema for a synthesis object. The marker fields are required, so a
    /// grammar-constrained decoder must emit them.
    /// </summary>
    private const string Json = """
                                {
                                  "type": "object",
                                  "properties": {
                                    "image_domain":        { "type": "string" },
                                    "metaphor":            { "type": "string" },
                                    "thesis":              { "type": "string" },
                                    "next_theme":          { "type": "string" },
                                    "connective_sentence": { "type": "string" },
                                    "remaining_dissensus": {
                                      "type": "string",
                                      "description": "The sharpest live tension this synthesis does not close. Write 'none' only if the conflict is genuinely resolved."
                                    },
                                    "self_check": {
                                      "type": "object",
                                      "properties": {
                                        "repeats_premise": {
                                          "type": "number",
                                          "description": "0.0-1.0: how much this synthesis merely restates the question rather than advancing it."
                                        },
                                        "grounded": { "type": "boolean" }
                                      },
                                      "required": ["repeats_premise", "grounded"]
                                    },
                                    "claims": {
                                      "type": "array",
                                      "description": "Grounded-synthesis claims (Reasoning Integrity Layer, substrate S2).",
                                      "items": {
                                        "type": "object",
                                        "properties": {
                                          "claim":        { "type": "string" },
                                          "support_from": { "type": "array", "items": { "type": "string" } },
                                          "attacked_by":  { "type": "array", "items": { "type": "string" } },
                                          "falsified_by": { "type": "string" },
                                          "confidence":   { "type": "number" }
                                        },
                                        "required": ["claim", "support_from", "falsified_by", "confidence"]
                                      }
                                    }
                                  },
                                  "required": [
                                    "image_domain",
                                    "metaphor",
                                    "thesis",
                                    "next_theme",
                                    "connective_sentence",
                                    "remaining_dissensus",
                                    "self_check"
                                  ]
                                }
                                """;

    /// <summary>
    /// The <c>response_format</c> value to place in the request body. Built once and shared;
    /// the parsed <see cref="JsonElement"/> is read-only and safe to reuse across requests.
    /// </summary>
    public static readonly JsonElement ResponseFormat = BuildResponseFormat();

    private static JsonElement BuildResponseFormat()
    {
        using var doc = JsonDocument.Parse($$"""
            {
              "type": "json_schema",
              "json_schema": {
                "name": "synthesis",
                "strict": true,
                "schema": {{Json}}
              }
            }
            """);
        // Clone so the element outlives the JsonDocument that owns its backing buffer.
        return doc.RootElement.Clone();
    }
}
