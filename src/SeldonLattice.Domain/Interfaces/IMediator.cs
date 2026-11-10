using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Domain.Models.Agents;

namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Interface for agents that mediate between two dialecticians in tension.
/// Mediators analyze the conflict and offer bridging insights without replacing either perspective.
/// Examples: the Mediator in the philosophy module
/// </summary>
public interface IMediator
{
    /// <summary>
    /// Analyzes the tension between two dialecticians' responses.
    /// Returns a MediationContext containing bridging insights that help move toward synthesis.
    /// </summary>
    /// <param name="dialectician1Response">First dialectician's response (e.g., the Proposer)..</param>
    /// <param name="dialectician2Response">Second dialectician's response (e.g., the Skeptic)..</param>
    /// <param name="theme">The theme being debated.</param>
    /// <param name="dialectician1History">Full conversation history of the first dialectician (includes opponent turns and own responses).</param>
    /// <param name="dialectician2History">Full conversation history of the second dialectician (includes opponent turns and own responses).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>MediationContext with bridging insights from this mediator's perspective.</returns>
    Task<MediationContext> AnalyzeTensionAsync(
        AgentResponse dialectician1Response,
        AgentResponse dialectician2Response,
        string theme,
        ChatHistory dialectician1History,
        ChatHistory dialectician2History,
        CancellationToken ct = default
    );
}
