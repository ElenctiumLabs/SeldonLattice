using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Infrastructure.Modules;

/// <summary>
/// Research orchestration layer. Runs the philosophy module first (thesis/antithesis on
/// theoretical foundations), then feeds its synthesis into any extra custom module created
/// at runtime (second-opinion passes), producing transparent multi-pass scholarship with
/// evidence chains. With no custom modules registered, it degrades gracefully to the
/// philosophy module's single-pass result.
/// </summary>
public class ResearchModule(
    IDialecticsModuleRegistry registry,
    IServiceScopeFactory scopeFactory,
    ILogger<ResearchModule> logger) : IDialecticsModule
{
    public string Key => "research";

    public string DisplayName => "Research Orchestration";

    public string Description =>
        "Multi-pass research orchestration. Chains the philosophy module with any custom " +
        "modules created at runtime to produce rigorous, multi-perspective synthesis with " +
        "transparent argumentation and evidence chains.";

    private static readonly string BuiltInKey = "philosophy";

    public async Task<DialogueLevelResult> ExecuteLevelAsync(
        int level, string theme, string sessionId, string rootTheme, CancellationToken ct = default)
    {
        // Resolve child modules from a fresh scope. The ResearchModule instance may
        // outlive the scope it was constructed in (the registry holds module instances
        // registered in a startup scope that is disposed before episodes run), and
        // modules are registered scoped - so resolve via the root IServiceScopeFactory,
        // which stays valid for the lifetime of the host.
        using var scope = scopeFactory.CreateScope();
        var philosophy = registry.GetModule(BuiltInKey, scope.ServiceProvider);

        logger.LogInformation("Research: running Philosophy level {Level} for theme '{Theme}'", level, theme);
        var philosophyResult = await philosophy.ExecuteLevelAsync(level, theme, sessionId, rootTheme, ct);

        // Second pass: any runtime-created custom module adds a fresh perspective.
        var customKey = ResolveCustomModuleKey();
        if (customKey is null)
            return philosophyResult;

        var secondTheme = !string.IsNullOrWhiteSpace(philosophyResult.NextTheme)
            ? philosophyResult.NextTheme
            : theme;
        var custom = registry.GetModule(customKey, scope.ServiceProvider);

        logger.LogInformation("Research: running custom module '{Key}' level {Level} for theme '{Theme}'", customKey, level, secondTheme);
        var customResult = await custom.ExecuteLevelAsync(level, secondTheme, sessionId, rootTheme, ct);

        var combinedMetaphor = BuildCombinedMetaphor(philosophyResult, customResult);
        var combinedThesis = !string.IsNullOrWhiteSpace(customResult.SynthesisThesis)
            ? customResult.SynthesisThesis
            : philosophyResult.SynthesisThesis;

        return new DialogueLevelResult
        {
            Metaphor = combinedMetaphor,
            SynthesisThesis = combinedThesis,
            NextTheme = customResult.NextTheme,
            ConnectiveSentence = BuildConnective(philosophyResult, customResult),
            Level = level,
        };
    }

    public async IAsyncEnumerable<DebateTurnEvent> ExecuteLevelStreamingAsync(
        int level, string theme, string sessionId, string rootTheme, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Resolve child modules from a fresh scope (see ExecuteLevelAsync note).
        using var scope = scopeFactory.CreateScope();
        var philosophy = registry.GetModule(BuiltInKey, scope.ServiceProvider);

        logger.LogInformation("Research: streaming Philosophy level {Level} for theme '{Theme}'", level, theme);
        await foreach (var evt in philosophy.ExecuteLevelStreamingAsync(level, theme, sessionId, rootTheme, ct))
        {
            yield return new DebateTurnEvent
            {
                Type = $"philosophy_{evt.Type}",
                Turn = evt.Turn,
                Content = evt.Content,
                Level = evt.Level,
                SpeakerId = evt.SpeakerId,
                DialecticRole = evt.DialecticRole,
                TriggeringSignal = evt.TriggeringSignal,
            };
        }

        var customKey = ResolveCustomModuleKey();
        if (customKey is null)
            yield break;

        var philosophyResult = await philosophy.ExecuteLevelAsync(level, theme, sessionId, rootTheme, ct);
        var secondTheme = !string.IsNullOrWhiteSpace(philosophyResult.NextTheme)
            ? philosophyResult.NextTheme
            : theme;
        var custom = registry.GetModule(customKey, scope.ServiceProvider);

        logger.LogInformation("Research: streaming custom module '{Key}' level {Level} for theme '{Theme}'", customKey, level, secondTheme);
        await foreach (var evt in custom.ExecuteLevelStreamingAsync(level, secondTheme, sessionId, rootTheme, ct))
        {
            yield return new DebateTurnEvent
            {
                Type = $"{customKey}_{evt.Type}",
                Turn = evt.Turn,
                Content = evt.Content,
                Level = evt.Level,
                SpeakerId = evt.SpeakerId,
                DialecticRole = evt.DialecticRole,
                TriggeringSignal = evt.TriggeringSignal,
            };
        }
    }

    /// <summary>
    /// The first registered module that is not a built-in key - i.e., a runtime-created
    /// custom module - to use as the second pass. Null when none exist yet.
    /// </summary>
    private string? ResolveCustomModuleKey()
    {
        foreach (var key in registry.GetAvailableModules())
        {
            if (key != BuiltInKey && key != Key)
                return key;
        }
        return null;
    }

    private static string BuildCombinedMetaphor(DialogueLevelResult first, DialogueLevelResult second)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(first.Metaphor))
            parts.Add($"[Philosophy] {first.Metaphor}");
        if (!string.IsNullOrWhiteSpace(second.Metaphor))
            parts.Add("[Custom] " + second.Metaphor);
        return parts.Count > 0
            ? string.Join("\n\n", parts)
            : "Multi-pass synthesis awaiting input.";
    }

    private static string BuildConnective(DialogueLevelResult first, DialogueLevelResult second)
    {
        if (!string.IsNullOrWhiteSpace(second.ConnectiveSentence))
            return second.ConnectiveSentence;
        if (!string.IsNullOrWhiteSpace(first.ConnectiveSentence))
            return first.ConnectiveSentence;
        return string.Empty;
    }
}
