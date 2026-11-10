using SeldonLattice.Application.Agents.Dialecticians.Philosophy;
using SeldonLattice.Application.Agents.Moderators;
using SeldonLattice.Domain.Configuration;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Infrastructure.Services;

namespace SeldonLattice.Infrastructure.Modules;

/// <summary>
/// The Philosophy Module - thesis/antithesis debate between the proposing and objecting
/// agents, with mediating moderation. The engine's one built-in demonstration domain:
/// philosophical reasoning observed as a dialectical ascent.
/// </summary>
public class PhilosophyModule(
    ProposerAgent proposer,
    SkepticAgent skeptic,
    MediatorAgent mediator,
    PhilosophyDialecticalAscentService ascentService) : IDialecticsModule
{
    public string Key => "philosophy";

    public string DisplayName => "Philosophical Reasoning";

    public string Description =>
        "Dialectical ascent through philosophical reasoning. The proposing agent (principled abstraction) " +
        "debates the objecting agent (practical grounding). A mediator facilitates their debate and " +
        "moderates their synthesis into metaphorical insights.";

    private DialecticalConfiguration CreateConfiguration() => new()
    {
        ModuleKey = Key,
        Dialectician1 = proposer,
        Dialectician2 = skeptic,
        Mediators = [mediator],
        Moderator = mediator,
        Dialectician1NodeType = "proposer_reply",
        Dialectician2NodeType = "skeptic_reply",
        SynthesisNodeType = "mediator_synthesis",
        Dialectician1SpeakerId = "proposer",
        Dialectician2SpeakerId = "skeptic",
        MediatorSpeakerIds = ["mediator"],
        MediatorMode = "Default"
    };

    public async Task<DialogueLevelResult> ExecuteLevelAsync(
        int level, string theme, string sessionId, string rootTheme, CancellationToken ct = default)
    {
        return await ascentService.AscendLevelAsync(level, theme, sessionId, CreateConfiguration(), ct);
    }

    public IAsyncEnumerable<DebateTurnEvent> ExecuteLevelStreamingAsync(
        int level, string theme, string sessionId, string rootTheme, CancellationToken ct = default)
    {
        return ascentService.AscendLevelStreamingAsync(level, theme, sessionId, CreateConfiguration(), ct);
    }
}
