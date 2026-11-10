using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Application.Extensions;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Agents;
using SeldonLattice.Domain.Options;

namespace SeldonLattice.Application.Agents.Mediators;

/// <summary>
/// Abstract base for mediator agents that follow the standard pattern:
/// build a prompt -> call the LLM -> extract the outermost JSON object ->
/// parse into <see cref="MediationContext"/>.
///
/// Subclasses implement only <see cref="BuildPrompt"/> with their role and
/// output schema. The LLM call, brace-extraction, JSON parsing, and fallback
/// are all handled here.
/// </summary>
public abstract class MediatorBase(Kernel kernel, IOptions<DialecticsOptions> options, IProviderRateLimiter rateLimiter) : IMediator
{
    private readonly DialecticsOptions _options = options.Value;
    /// <summary>
    /// Builds the full prompt string for this mediator's role.
    /// Receives the same parameters as <see cref="AnalyzeTensionAsync"/> plus
    /// the pre-formatted history strings.
    /// </summary>
    protected abstract string BuildPrompt(
        AgentResponse dialectician1Response,
        AgentResponse dialectician2Response,
        string theme,
        string history1Formatted,
        string history2Formatted);

    /// <summary>
    /// Extracts the primary insight string from the parsed JSON root element.
    /// Different mediators use different field names (e.g. "mediator_insight" vs "harmonizing_insight").
    /// </summary>
    protected abstract string ExtractInsight(JsonElement root);

    /// <summary>
    /// Extracts the tensions/keywords array from the parsed JSON root element.
    /// </summary>
    protected abstract string[] ExtractTensions(JsonElement root);

    /// <summary>Fallback insight string used when JSON parsing fails.</summary>
    protected abstract string FallbackInsight { get; }

    /// <summary>Fallback tension tag used when JSON parsing fails.</summary>
    protected abstract string FallbackTension { get; }

    public async Task<MediationContext> AnalyzeTensionAsync(
        AgentResponse dialectician1Response,
        AgentResponse dialectician2Response,
        string theme,
        ChatHistory dialectician1History,
        ChatHistory dialectician2History,
        CancellationToken ct = default)
    {
        var history1 = dialectician1History.FormatForMediator(_options.MediatorMessageTruncateLength);
        var history2 = dialectician2History.FormatForMediator(_options.MediatorMessageTruncateLength);

        var prompt = BuildPrompt(dialectician1Response, dialectician2Response, theme, history1, history2);

        // The parsing contract goes at the top of this single-message prompt so the model
        // knows its reply is brace-extracted as one JSON object before any role/anchor text.
        // Skipped when the gate is off (small local models ignore it).
        if (PromptContracts.Enabled)
            prompt = $"{PromptContracts.OutputContract}\n\n{prompt}";

        var history = new ChatHistory(prompt).EnsureAtLeastOneUserMessage();
        await rateLimiter.WaitForSlotAsync(ct);
        var chatCompletion = kernel.GetRequiredService<IChatCompletionService>();
        var response = await chatCompletion.GetChatMessageContentAsync(history, cancellationToken: ct);
        var raw = response.Content?.Trim() ?? string.Empty;

        // Strip to the outermost JSON object - models sometimes wrap with extra text.
        var startIdx = raw.IndexOf('{');
        var endIdx = raw.LastIndexOf('}');
        if (startIdx >= 0 && endIdx > startIdx)
            raw = raw[startIdx..(endIdx + 1)];

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            return new MediationContext(
                ExtractInsight(root),
                string.Empty,
                _options.DefaultTensionIntensity,
                ExtractTensions(root));
        }
        catch
        {
            return new MediationContext(
                MediatorAInsight: string.IsNullOrWhiteSpace(raw) ? FallbackInsight : raw,
                MediatorBInsight: string.Empty,
                TensionIntensity: _options.DefaultTensionIntensity,
                UnresolvedTensions: [FallbackTension]);
        }
    }
}
