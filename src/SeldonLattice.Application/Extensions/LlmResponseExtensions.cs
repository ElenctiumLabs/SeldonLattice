namespace SeldonLattice.Application.Extensions;

using System.Text.RegularExpressions;

public static class LlmResponseExtensions
{
    // Pre-compile the regex for performance since it will run on every turn
    private static readonly Regex ThinkTagRegex = new Regex(
        @"<think>[\s\S]*?</think>", 
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string StripReasoningTags(this string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse)) 
            return string.Empty;
        
        return ThinkTagRegex.Replace(rawResponse, string.Empty).Trim();
    }
}