using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Domain.Models.Agents;
using SeldonLattice.Domain.Models.Results;

namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Interface for agents that synthesize and moderate the resolution of dialectical debates.
/// Moderators move beyond mere mediation to extract higher-level truths and next inquiry themes.
/// Examples: the Mediator in the philosophy module
/// </summary>
public interface IModerator
{
    /// <summary>
    /// Creates a conversation history for synthesizing the debate into a metaphor.
    /// The moderator has access to both dialecticians' final insights and mediation context.
    /// </summary>
    /// <param name="theme">The theme that was debated.</param>
    /// <param name="previousResponses">Final responses from all participating agents (dialecticians, mediators).</param>
    /// <param name="moderationFocus">Subject-specific focus for this moderation (e.g., "How does this serve creative transformation?").</param>
    /// <param name="cyclePhase">Current phase of the debate cycle (emergence/growth/crisis/saturation/renewal).</param>
    /// <returns>A ChatHistory ready for synthesis.</returns>
    ChatHistory CreateSynthesisHistory(
        string theme,
        AgentResponse[] previousResponses,
        string moderationFocus,
        string cyclePhase = "growth"
    );

    /// <summary>
    /// Generates the synthesis metaphor and thesis from the conversation history.
    /// </summary>
    /// <param name="history">The synthesis conversation history.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>MetaphorResult containing metaphor, image domain, and thesis.</returns>
    Task<MetaphorResult> SynthesizeAsync(ChatHistory history, CancellationToken ct = default);
}
