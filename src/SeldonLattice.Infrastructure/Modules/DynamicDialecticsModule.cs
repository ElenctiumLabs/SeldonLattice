using Microsoft.Extensions.DependencyInjection;
using SeldonLattice.Application.Agents.Dialecticians.Philosophy;
using SeldonLattice.Application.Agents.Moderators;
using SeldonLattice.Domain.Configuration;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Infrastructure.Services;

namespace SeldonLattice.Infrastructure.Modules;

/// <summary>
/// A dynamically-created dialectics module that can be registered at runtime
/// via the API. Uses an existing ascent service as the engine but allows
/// configurable agent selection from the pre-registered agent pool.
/// </summary>
public sealed class DynamicDialecticsModule(
    IServiceProvider serviceProvider,
    DialecticalAscentServiceBase ascentService,
    ModuleConfiguration configuration)
    : IDialecticsModule
{
    public string Key { get; } = configuration.Key;
    public string DisplayName { get; } = configuration.Name;
    public string Description { get; } = configuration.Description ?? "Custom dialectics module";

    /// <summary>
    /// Maps role names to their DI service types for agent resolution.
    /// Faculty roles, not personas: a custom module composes the debate from these roles.
    /// </summary>
    private static readonly Dictionary<string, Type> AgentTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Philosophy module (faculty roles)
        ["proposer"] = typeof(ProposerAgent),
        ["skeptic"] = typeof(SkepticAgent),
        ["mediator"] = typeof(MediatorAgent),
    };

    public async Task<DialogueLevelResult> ExecuteLevelAsync(
        int level, string theme, string sessionId, string rootTheme, CancellationToken ct = default)
    {
        var config = BuildConfiguration();
        return await ascentService.AscendLevelAsync(level, theme, sessionId, config, ct);
    }

    public IAsyncEnumerable<DebateTurnEvent> ExecuteLevelStreamingAsync(
        int level, string theme, string sessionId, string rootTheme, CancellationToken ct = default)
    {
        var config = BuildConfiguration();
        return ascentService.AscendLevelStreamingAsync(level, theme, sessionId, config, ct);
    }

    private DialecticalConfiguration BuildConfiguration()
    {
        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;

        var agents = configuration.Agents;
        if (agents.Length < 2)
            throw new InvalidOperationException("Custom module requires at least 2 agents");

        var dialectician1 = ResolveDialectician(sp, agents[0]);
        var dialectician2 = ResolveDialectician(sp, agents[1]);

        var mediators = Array.Empty<IMediator>();
        if (configuration.MediatorConfig?.MediatorNames?.Length > 0)
        {
            var mediatorList = new List<IMediator>();
            foreach (var mediatorName in configuration.MediatorConfig.MediatorNames)
            {
                if (TryResolveMediator(sp, mediatorName, out var mediator))
                    mediatorList.Add(mediator);
            }
            mediators = [.. mediatorList];
        }

        IModerator? moderator = null;
        if (configuration.ModeratorConfig?.ModeratorName != null)
        {
            moderator = ResolveModerator(sp, configuration.ModeratorConfig.ModeratorName);
        }

        return new DialecticalConfiguration
        {
            ModuleKey = Key,
            Dialectician1 = dialectician1,
            Dialectician2 = dialectician2,
            Mediators = mediators,
            Moderator = moderator,
            Dialectician1NodeType = "reply_1",
            Dialectician2NodeType = "reply_2",
            SynthesisNodeType = "synthesis",
            Dialectician1SpeakerId = agents[0].Name ?? "speaker_1",
            Dialectician2SpeakerId = agents[1].Name ?? "speaker_2",
            MediatorSpeakerIds = configuration.MediatorConfig?.MediatorNames ?? [],
            MediatorMode = configuration.MediatorConfig?.Mode ?? "Default"
        };
    }

    private static IDialectician ResolveDialectician(IServiceProvider sp, AgentConfiguration agentDef)
    {
        var agentType = ResolveAgentType(agentDef.Name, agentDef.Framework);
        if (agentType is null)
            throw new InvalidOperationException($"Unknown agent: {agentDef.Name}");

        var agent = sp.GetRequiredService(agentType) as IDialectician;
        if (agent is null)
            throw new InvalidOperationException($"Agent '{agentDef.Name}' does not implement IDialectician");

        return agent;
    }

    private static IModerator ResolveModerator(IServiceProvider sp, string name)
    {
        var agentType = ResolveAgentType(name, null);
        if (agentType is null)
            throw new InvalidOperationException($"Unknown moderator: {name}");

        var moderator = sp.GetRequiredService(agentType) as IModerator;
        if (moderator is null)
            throw new InvalidOperationException($"Agent '{name}' does not implement IModerator");

        return moderator;
    }

    private static bool TryResolveMediator(IServiceProvider sp, string name, out IMediator mediator)
    {
        mediator = null!;
        var agentType = ResolveAgentType(name, null);
        if (agentType is null)
            return false;

        var m = sp.GetRequiredService(agentType) as IMediator;
        if (m is null)
            return false;

        mediator = m;
        return true;
    }

    private static Type? ResolveAgentType(string? name, string? framework)
    {
        if (!string.IsNullOrEmpty(name) && AgentTypeMap.TryGetValue(name, out var type))
            return type;
        if (!string.IsNullOrEmpty(framework) && AgentTypeMap.TryGetValue(framework, out var fType))
            return fType;
        return null;
    }
}
