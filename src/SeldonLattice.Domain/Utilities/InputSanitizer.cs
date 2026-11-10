using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SeldonLattice.Domain.Utilities;

/// <summary>
/// Utility for sanitizing and structurally isolating untrusted external inputs
/// (user prompts, ingested contracts, PDFs, external API responses) to prevent prompt injection.
/// </summary>
public static class InputSanitizer
{
    private static readonly Regex ControlCharRegex = new(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.Compiled);
    private static readonly Regex CDATACloseRegex = new(@"\]\]\>", RegexOptions.Compiled);
    private static readonly Regex TagCloseRegex = new(@"\<\s*\/\s*untrusted_input_context\s*\>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Cleans raw text by stripping disallowed control characters and neutralizing CDATA / tag escape attempts.
    /// </summary>
    public static string SanitizeText(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return string.Empty;

        // Strip disallowed control characters
        var cleaned = ControlCharRegex.Replace(rawText, string.Empty);

        // Neutralize CDATA escaping attempts (replace ]]> with ] ] >)
        cleaned = CDATACloseRegex.Replace(cleaned, "] ] >");

        // Neutralize boundary closing tag attempts
        cleaned = TagCloseRegex.Replace(cleaned, "</untrusted_input_context_escaped>");

        return cleaned;
    }

    /// <summary>
    /// Wraps untrusted text in a strict structural boundary container with CDATA isolation and hash anchoring.
    /// </summary>
    /// <param name="untrustedText">The raw input text from user or external source.</param>
    /// <param name="sourceName">Source identifier (e.g. "user_prompt", "ingested_contract.pdf").</param>
    /// <returns>Structurally isolated input string safe for prompt interpolation.</returns>
    public static string WrapUntrustedInput(string? untrustedText, string sourceName = "untrusted_input")
    {
        var sanitized = SanitizeText(untrustedText);
        var inputHash = ComputeSha256(sanitized);

        var sb = new StringBuilder();
        sb.AppendLine($"<untrusted_input_context source=\"{SanitizeAttribute(sourceName)}\" hash=\"sha256-{inputHash}\">");
        sb.AppendLine("<![CDATA[");
        sb.AppendLine(sanitized);
        sb.AppendLine("]]>");
        sb.AppendLine("</untrusted_input_context>");

        return sb.ToString();
    }

    private static string SanitizeAttribute(string attr)
    {
        return attr.Replace("\"", "'").Replace("<", "").Replace(">", "");
    }

    private static string ComputeSha256(string data)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
