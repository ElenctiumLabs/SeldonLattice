using System.Text.Json;
using SeldonLattice.Domain.Models.Results;

namespace SeldonLattice.Application.Audit;

/// <summary>
/// Parser for the structured `claims` channel of the grounded-synthesis contract.
/// Deliberately tolerant: models produce
/// near-JSON, so the parser accepts extra keys, missing optional fields, and coerces
/// confidence from either a number or a "0.85"-style string. Every claim that parses
/// becomes a <see cref="ClaimDraft"/>; a malformed claim is dropped with a warning the
/// caller is free to log. This is the primary (structured) path - the post-hoc LLM
/// extractor is the fallback for legacy nodes.
/// </summary>
public static class ClaimsContractParser
{
    public const double DefaultConfidence = 0.7;

    /// <summary>
    /// Attempts to parse a `claims` array from raw synthesis text that may be wrapped in
    /// markdown fences or trailing prose. False when no usable claims array is present.
    /// </summary>
    public static bool TryParseClaims(string rawJson, out IReadOnlyList<ClaimDraft> claims)
    {
        claims = [];
        if (string.IsNullOrWhiteSpace(rawJson))
            return false;

        var cleaned = StripCodeFences(rawJson);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(cleaned);
        }
        catch (JsonException)
        {
            // Pull the first {...} JSON object out of mixed prose, best-effort.
            var start = cleaned.IndexOf('{');
            var end = cleaned.LastIndexOf('}');
            if (start < 0 || end <= start)
                return false;
            try { doc = JsonDocument.Parse(cleaned[start..(end + 1)]); }
            catch (JsonException) { return false; }
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
                return TryParseClaims(root, out claims);

            if (!root.TryGetProperty("claims", out var claimsElement) ||
                claimsElement.ValueKind != JsonValueKind.Array)
                return false;

            return TryParseClaims(claimsElement, out claims);
        }
    }

    /// <summary>
    /// Parses an already-located JSON array of claim objects.
    /// </summary>
    public static bool TryParseClaims(JsonElement claimsElement, out IReadOnlyList<ClaimDraft> claims)
    {
        claims = [];
        if (claimsElement.ValueKind != JsonValueKind.Array || claimsElement.GetArrayLength() == 0)
            return false;

        var drafts = new List<ClaimDraft>();
        foreach (var item in claimsElement.EnumerateArray())
        {
            if (!TryParseSingleClaim(item, out var draft))
                continue;
            drafts.Add(draft);
        }

        claims = drafts;
        return drafts.Count > 0;
    }

    private static bool TryParseSingleClaim(JsonElement item, out ClaimDraft draft)
    {
        draft = null!;
        if (item.ValueKind != JsonValueKind.Object)
            return false;

        var claimText = ReadString(item, "claim") ?? ReadString(item, "claim_text");
        if (string.IsNullOrWhiteSpace(claimText))
            return false;

        var supportedBy = ReadStringArray(item, "support_from");
        var attackedBy = ReadStringArray(item, "attacked_by");
        var falsifiedBy = ReadString(item, "falsified_by");
        var confidence = ReadConfidence(item, "confidence");

        draft = new ClaimDraft(claimText, supportedBy, attackedBy, falsifiedBy, confidence);
        return true;
    }

    private static string? ReadString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            _ => null
        };
    }

    private static string[] ReadStringArray(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array)
            return [];

        var values = new List<string>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                values.Add(item.GetString()!);
        }
        return [.. values];
    }

    private static float ReadConfidence(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el))
            return (float)DefaultConfidence;

        double value;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out value))
            return (float)Math.Clamp(value, 0.0, 1.0);

        if (el.ValueKind == JsonValueKind.String &&
            el.GetString() is { } s &&
            double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value))
            return (float)Math.Clamp(value, 0.0, 1.0);

        return (float)DefaultConfidence;
    }

    private static string StripCodeFences(string json)
    {
        var trimmed = json.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
                trimmed = trimmed[(firstNewline + 1)..];
        }
        if (trimmed.EndsWith("```"))
            trimmed = trimmed[..^3];

        trimmed = trimmed.Trim();
        if (trimmed.StartsWith("```"))
            return StripCodeFences(trimmed);
        return trimmed;
    }
}