using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Domain.Models.Agents;

namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Interface for a subnode/fault-injector agent that disrupts premature synthesis
/// by injecting shadow, repressed drive, or unconscious content. Activated when
/// the aporia score drops below threshold (consensus too easy), preventing
/// spiritual bypassing or shallow agreement.
/// </summary>
public interface ISubnodeAgent
{
    /// <summary>
    /// Injects shadow content into the debate. Called when aporia is low
    /// (premature consensus detected). The subnode destabilizes idealized
    /// positions by surfacing repressed drives, ego defenses, or unconscious
    /// material that both dialecticians have avoided.
    /// </summary>
    /// <param name="thesisContent">The thesis dialectician's most recent response.</param>
    /// <param name="antithesisContent">The antithesis dialectician's most recent response.</param>
    /// <param name="theme">The theme being debated.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An AgentResponse containing the shadow injection content.</returns>
    Task<AgentResponse> InjectShadowAsync(
        string thesisContent,
        string antithesisContent,
        string theme,
        CancellationToken ct = default
    );

    /// <summary>
    /// Creates a conversation history for shadow injection.
    /// </summary>
    ChatHistory CreateShadowHistory(
        string thesisContent,
        string antithesisContent,
        string theme
    );
}
