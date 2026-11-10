using System.Text.Json;
using System.Text.RegularExpressions;
using SeldonLattice.Application.Extensions;
using SeldonLattice.Domain.Models.Agents;

namespace SeldonLattice.Application.Agents;

/// <summary>
/// Parses a raw LLM turn into a clean spoken <see cref="AgentResponse.Content"/> and a
/// private <see cref="AgentResponse.Reasoning"/> scratchpad.
///
/// This is the schema boundary fix for the "scaffold leaking into the role's voice"
/// problem: agents are prompted to do their bookkeeping (identify the assumption being
/// targeted, name the opponent's strongest claim, line up a counterexample, etc.) in a
/// "reasoning" JSON field, and to write ONLY in-character prose in a "response" field.
/// Only "response" is ever surfaced as Content - "reasoning" never reaches the
/// transcript, the other agent's history, or the client. A negative instruction alone
/// ("never use bracketed labels") is one prompt edit away from breaking; a schema
/// boundary survives future prompt edits because there is structurally nowhere for the
/// scaffold to leak to.
///
/// Small/local models sometimes ignore the JSON instruction entirely. For that case,
/// <see cref="Parse"/> falls back to treating the whole reply as spoken content, but
/// still strips the two concrete leak patterns observed in production logs -
/// bracketed meta-labels like "[NEW PREMISE]" and bold meta-headers like
/// "**Refutation with Counterexample:**" - so degraded output still reads as prose
/// instead of a raw template dump.
/// </summary>
public static class AgentResponseParser
{
    public static AgentResponse Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new AgentResponse(string.Empty);

        var trimmed = StripCodeFences(raw.StripReasoningTags());

        // Primary format: labeled sections [REASONING] / [RESPONSE].
        // Small models follow this far more reliably than JSON.
        if (TryParseLabeledSections(trimmed, out var labeledParsed))
            return labeledParsed;

        if (TryParseJson(trimmed, out var parsed))
            return parsed;

        // Model didn't return a requested format. This is expected for small/prose-only
        // models - not an error. Try the remaining fallbacks; if none recover a private
        // reasoning section, the whole reply is treated as spoken content.
        System.Diagnostics.Debug.WriteLine($"No structured format detected; falling back. First 200 chars: {trimmed.Substring(0, Math.Min(200, trimmed.Length))}");

        // Pattern: model returns markdown-style labels "**Reasoning**: ..." followed by
        // "**Response**: ..." instead of the requested format. Keep the reasoning
        // private and surface only the response, mirroring the schema boundary.
        if (TryParseMarkdownLabels(trimmed, out var markdownParsed))
            return markdownParsed;

        // Pattern: text starts with a JSON reasoning object (no "response" field) followed
        // by spoken content.  Example from production:
        //   {"reasoning": "..."}
        //   "spoken response here"
        // or:
        //   {"reasoning": "..."}
        //   spoken response here
        if (trimmed.Length > 0 && trimmed[0] == '{')
        {
            var depth = 0;
            var jsonEnd = -1;
            for (var i = 0; i < trimmed.Length; i++)
            {
                if (trimmed[i] == '{') depth++;
                else if (trimmed[i] == '}') { depth--; if (depth == 0) { jsonEnd = i; break; } }
            }

            if (jsonEnd > 0)
            {
                var jsonPart = trimmed[..(jsonEnd + 1)];
                var restPart = trimmed[(jsonEnd + 1)..].Trim().Trim('"').Trim();

                try
                {
                    using var doc = JsonDocument.Parse(jsonPart);
                    var root = doc.RootElement;
                    var reasoning = root.TryGetProperty("reasoning", out var rs) ? rs.GetString() : null;
                    // Only use this path if the JSON had reasoning but no response field
                    var hasResponse = root.TryGetProperty("response", out _);

                    if (!hasResponse && !string.IsNullOrWhiteSpace(restPart))
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Leading-JSON-reasoning extraction succeeded, content length: {restPart.Length}");
                        return new AgentResponse(SanitizeContent(restPart), reasoning);
                    }
                }
                catch (JsonException ex)
                {
                    // The leading-JSON-reasoning extraction is best-effort; if the JSON is
                    // malformed, fall through to the final fallback below.
                    System.Diagnostics.Debug.WriteLine($"Leading-JSON-reasoning extraction failed: {ex.Message}");
                }
            }
        }

        // Final fallback: if the text looks like it contains a JSON object with reasoning/response,
        // try one more time to extract just the response text
        if (trimmed.Contains("\"reasoning\"") && trimmed.Contains("\"response\""))
        {
            System.Diagnostics.Debug.WriteLine("Detected reasoning/response pattern in fallback, attempting extraction");
            var finalMatch = Regex.Match(trimmed, @"""response""\s*:\s*""([\s\S]*?)(?=""\s*,\s*\n\s*""reasoning""|""\s*,\s*\n\s*$)", RegexOptions.Singleline);
            if (finalMatch.Success)
            {
                var extractedContent = Regex.Unescape(finalMatch.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(extractedContent))
                {
                    System.Diagnostics.Debug.WriteLine($"Final fallback extraction succeeded, content length: {extractedContent.Length}");
                    return new AgentResponse(SanitizeContent(extractedContent));
                }
            }
        }
        
        return new AgentResponse(SanitizeContent(trimmed));
    }

    /// <summary>
    /// Primary parser: looks for [REASONING] and [RESPONSE] section markers.
    /// Small models follow this labeled-section format far more reliably than JSON.
    /// Returns false if the markers are not present.
    /// </summary>
    private static bool TryParseLabeledSections(string text, out AgentResponse result)
    {
        result = new AgentResponse(string.Empty);
        var responseIdx = Regex.Match(text, @"^\s*\[RESPONSE\]\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!responseIdx.Success)
        {
            // Also handle inline variant: [RESPONSE] followed by content on the same line
            responseIdx = Regex.Match(text, @"^\s*\[RESPONSE\]\s*", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (!responseIdx.Success)
                return false;
        }

        var responseStart = responseIdx.Index + responseIdx.Length;
        var responseText = text[responseStart..].Trim();
        if (string.IsNullOrWhiteSpace(responseText))
            return false;

        // Reasoning is everything between [REASONING] and [RESPONSE], if present.
        var reasoningIdx = Regex.Match(text, @"^\s*\[REASONING\]\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        string? reasoningText = null;
        if (reasoningIdx.Success && reasoningIdx.Index < responseIdx.Index)
        {
            var reasoningStart = reasoningIdx.Index + reasoningIdx.Length;
            reasoningText = text.Substring(reasoningStart, responseIdx.Index - reasoningStart).Trim();
        }

        result = new AgentResponse(
            SanitizeContent(responseText),
            string.IsNullOrWhiteSpace(reasoningText) ? null : SanitizeContent(reasoningText));
        return true;
    }

    /// <summary>
    /// Handles the markdown-label leak where the model returns "**Reasoning**: ..." and
    /// "**Response**: ..." instead of the requested format. Splits the reasoning off so
    /// it stays private, mirroring the schema boundary. Returns false if the labels are
    /// not present.
    /// </summary>
    private static bool TryParseMarkdownLabels(string text, out AgentResponse result)
    {
        result = new AgentResponse(string.Empty);
        var reasoning = Regex.Match(text, @"^\s*\*\*\s*Reasoning\s*\**:?\s*", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        var response = Regex.Match(text, @"^\s*\*\*\s*Response\s*\**:?\s*", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (!reasoning.Success || !response.Success || response.Index < reasoning.Index)
            return false;

        var reasoningEnd = reasoning.Index + reasoning.Length;
        var reasoningText = text.Substring(reasoningEnd, response.Index - reasoningEnd).Trim();
        var responseText = text.Substring(response.Index + response.Length).Trim();
        if (string.IsNullOrWhiteSpace(responseText))
            return false;

        result = new AgentResponse(
            SanitizeContent(responseText),
            string.IsNullOrWhiteSpace(reasoningText) ? null : SanitizeContent(reasoningText));
        return true;
    }

    private static bool TryParseJson(string text, out AgentResponse result)
    {
        result = new AgentResponse(string.Empty);
        if (text.Length == 0 || text[0] != '{')
            return false;

        // Small models often emit JSON with unescaped newlines inside string values,
        // which makes JsonDocument.Parse fail. Normalize first.
        var normalized = NormalizeJson(text);

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            var root = doc.RootElement;
            
            // Try to get the response field - it might be a string or could be missing
            var content = root.TryGetProperty("response", out var r) ? r.GetString() : null;
            var reasoning = root.TryGetProperty("reasoning", out var rs) ? rs.GetString() : null;

            // If response is null or whitespace, this isn't valid JSON for our purposes
            if (string.IsNullOrWhiteSpace(content))
            {
                System.Diagnostics.Debug.WriteLine($"JSON parsed but response field is null or whitespace");
                return false;
            }

            result = new AgentResponse(SanitizeContent(content), reasoning);
            System.Diagnostics.Debug.WriteLine($"JSON parsed successfully, content length: {content.Length}");
            return true;
        }
        catch (JsonException ex)
        {
            // Log the parsing failure for debugging
            System.Diagnostics.Debug.WriteLine($"JSON parsing failed: {ex.Message}. Text length: {text.Length}");
            
            // Fallback: try to manually extract the response field using regex
            // This handles cases where the JSON might have minor formatting issues
            // The regex handles multi-line strings with escaped characters
            var responseMatch = Regex.Match(normalized, @"""response""\s*:\s*""((?:[^""\\]|\\.)*)(?=""\s*,?\s*""reasoning""|""\s*})", RegexOptions.Singleline);
            if (responseMatch.Success)
            {
                var extractedContent = Regex.Unescape(responseMatch.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(extractedContent))
                {
                    System.Diagnostics.Debug.WriteLine($"Regex fallback succeeded, extracted content length: {extractedContent.Length}");
                    result = new AgentResponse(SanitizeContent(extractedContent));
                    return true;
                }
            }
            
            // Second fallback: try extracting response when reasoning comes FIRST and response comes SECOND
            var responseAfterReasoning = Regex.Match(normalized, @"""response""\s*:\s*""([\s\S]*?)""\s*\}?\s*$", RegexOptions.Singleline);
            if (responseAfterReasoning.Success)
            {
                var extractedContent = UnescapeJsonString(responseAfterReasoning.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(extractedContent))
                {
                    System.Diagnostics.Debug.WriteLine($"Response-after-reasoning regex fallback succeeded, content length: {extractedContent.Length}");
                    result = new AgentResponse(SanitizeContent(extractedContent));
                    return true;
                }
            }

            return false;
        }
    }

    private static string UnescapeJsonString(string raw)
    {
        try
        {
            return Regex.Unescape(raw);
        }
        catch
        {
            return raw.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\t", "\t");
        }
    }

    /// <summary>
    /// Escapes unescaped newlines and carriage returns inside JSON string values.
    /// Small models frequently emit JSON like {"key": "line1\nline2"} where the
    /// newlines are literal characters instead of the \n escape sequence, which
    /// makes System.Text.Json.JsonDocument.Parse reject the entire object.
    /// </summary>
    private static string NormalizeJson(string json)
    {
        var sb = new System.Text.StringBuilder(json.Length);
        var inString = false;
        var escaped = false;

        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];

            if (escaped)
            {
                sb.Append(c);
                escaped = false;
                continue;
            }

            if (inString)
            {
                if (c == '\\')
                {
                    sb.Append(c);
                    escaped = true;
                    continue;
                }
                if (c == '"')
                {
                    sb.Append(c);
                    inString = false;
                    continue;
                }
                // Inside a string value, escape raw newlines/CRs that the model
                // forgot to escape.
                if (c == '\n')
                {
                    sb.Append("\\n");
                    continue;
                }
                if (c == '\r')
                {
                    sb.Append("\\r");
                    continue;
                }
                sb.Append(c);
                continue;
            }

            // Outside a string
            if (c == '"')
            {
                sb.Append(c);
                inString = true;
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static string StripCodeFences(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;

        var firstNewline = text.IndexOf('\n');
        var body = firstNewline >= 0 ? text[(firstNewline + 1)..] : text;
        return body.EndsWith("```", StringComparison.Ordinal) ? body[..^3].TrimEnd() : body;
    }

    /// <summary>
    /// Strips bracketed meta-labels (e.g. "[DIALECTICAL INVERSION: FLIP THE ENTIRE
    /// FRAMEWORK]"), bold meta-headers (e.g. "**Verbatim Opponent's Strongest
    /// Claim:**"), and orphaned bold delimiters (bare "**" on its own line) from the
    /// start of lines. Handles both "**Label:**" and "**Label**:" colon placements
    /// so no stray ":" remains. These are the concrete patterns the diagnostic
    /// identified: the model echoing scaffold instruction syntax back as literal
    /// formatting instead of treating it as invisible.
    /// </summary>
    private static string SanitizeContent(string text)
    {
        var cleaned = Regex.Replace(text, @"^[ \t]*\[[A-Za-z0-9 :,'\-]{2,60}\][ \t]*", "", RegexOptions.Multiline);
        cleaned = Regex.Replace(cleaned, @"^[ \t]*\*\*[^*\n]{2,80}:?\*\*:?[ \t]*", "", RegexOptions.Multiline);
        // Strip orphaned bold delimiters: bare "**" on its own line (no label text).
        cleaned = Regex.Replace(cleaned, @"^[ \t]*\*\*[ \t]*$", "", RegexOptions.Multiline);
        // Strip leading colon: some models emit ": text" after [RESPONSE] marker.
        cleaned = Regex.Replace(cleaned, @"^[ \t]*:[ \t]*", "", RegexOptions.Multiline);
        return cleaned.Trim();
    }
}