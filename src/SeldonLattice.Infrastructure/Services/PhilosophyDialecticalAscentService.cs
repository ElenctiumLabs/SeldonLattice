using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using SeldonLattice.Application.Agents.Moderators;
using SeldonLattice.Application.Memory;
using SeldonLattice.Application.Services;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models;
using SeldonLattice.Domain.Models.Cycle;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Domain.Options;

namespace SeldonLattice.Infrastructure.Services;

/// <summary>
/// Dialectical ascent for the philosophy module (proposer, skeptic, mediator).
/// Stalemate is handled by mediator interjections. No per-turn mediation.
/// </summary>
public class PhilosophyDialecticalAscentService(
    ILogger<PhilosophyDialecticalAscentService> logger,
    IMerkleStore merkleStore,
    ISessionService sessionService,
    Kernel kernel,
    PhilosophyDialecticsOptions options,
    CyclePhaseDetector phaseDetector,
    DialogueQualityGates qualityGates,
    IClaimMemoryRetriever? claimMemoryRetriever = null,
    LlmOptions? llmOptions = null)
    : DialecticalAscentServiceBase(logger, merkleStore, sessionService, kernel, options, phaseDetector, qualityGates, claimMemoryRetriever: claimMemoryRetriever, llmOptions: llmOptions)
{
    /// <summary>
    /// Resolves the moderator name from the agent instance.
    /// </summary>
    protected override string GetModeratorName(IModerator moderator) => moderator switch
    {
        MediatorAgent => "mediator",
        _ => moderator.GetType().Name.ToLowerInvariant()
    };

    /// <summary>
    /// On stalemate: the Mediator generates an interjection to break the deadlock.
    /// The interjection is injected into D2's history as if the mediator spoke.
    /// </summary>
    protected override async IAsyncEnumerable<DebateTurnEvent> OnStalemateAsync(
        StalemateSignal signal, string theme, int turn, int level, [EnumeratorCancellation] CancellationToken ct)
    {
        if (_config.Mediators.FirstOrDefault(m => m is MediatorAgent) is not MediatorAgent mediator)
            yield break;

        var interjection = await mediator.GenerateInterjectionAsync(
            signal, theme, D1LastText, D2LastText,
            d1Name: _config.Dialectician1SpeakerId,
            d2Name: _config.Dialectician2SpeakerId, ct);

        // Surface the interjection to both dialecticians on their next turn rather than
        // appending it to each history as a standalone message. A standalone user message
        // after the last assistant turn would create consecutive user roles, which
        // llama.cpp's chat template rejects ("Conversation roles must alternate..."),
        // killing the stream right after this mediation event. D2LastText is deliberately
        // left unchanged - the next D1 turn will address the Skeptic (D2's last actual
        // speech), not the Mediator.
        InjectMediatorNote($"[Mediator interjects]: {interjection}");

        yield return new DebateTurnEvent
        {
            Type = "mediator_interjection",
            SpeakerId = "mediator",
            Turn = turn,
            Content = interjection,
            Level = level,
            TriggeringSignal = signal,
            DialecticRole = DialecticRole.Mediation,
        };
    }

    /// <summary>
    /// No per-turn mediation in the philosophy module.
    /// </summary>
    protected override IAsyncEnumerable<DebateTurnEvent> OnPerTurnMediationAsync(
        string theme, int turn, int level, CancellationToken ct)
        => EmptyEvents();
}
