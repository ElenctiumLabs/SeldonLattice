using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Domain.Models.Agents;
using SeldonLattice.Domain.Models.Cycle;

namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Interface for any philosophical agent that participates in dialectical exchange.
/// Dialecticians represent two perspectives in creative tension, debating until equilibrium.
/// Examples: faculty roles in a dialectical exchange (e.g., a proposer and a skeptic)
/// </summary>
public interface IDialectician
{
    /// <summary>
    /// Creates a new conversation history initialized with the agent's system prompt.
    /// The agent is configured to debate the given theme at the specified intensity level.
    /// </summary>
    /// <param name="theme">The philosophical question to explore.</param>
    /// <param name="rootTheme">Optional root question of the entire session for domain anchoring.</param>
    /// <param name="move">Optional rhetorical move to enforce structural variety this turn.</param>
    /// <param name="intensity">Intensity level (1-10). Higher values unlock advanced reasoning.</param>
    /// <returns>A ChatHistory initialized with the agent's system role and instructions.</returns>
    ChatHistory CreateHistory(
        string theme,
        string? rootTheme = null,
        RhetoricalMove? move = null,
        int intensity = -1
    );

    /// <summary>
    /// Generates a response from the agent based on the provided conversation history.
    /// Uses Semantic Kernel's chat completion service, then parses the response into
    /// clean spoken content and optional private reasoning via AgentResponseParser.
    /// </summary>
    /// <param name="history">The dialogue context including system prompt and previous messages.</param>
    /// <param name="ct">Cancellation token for the async operation.</param>
    /// <returns>The agent's turn, split into spoken content and (if available) private reasoning.</returns>
    Task<AgentResponse> GetResponseAsync(ChatHistory history, CancellationToken ct = default);

    /// <summary>
    /// Generates a response using boosted sampler parameters to apply ascent pressure
    /// without adding any extra tokens to the context window.
    /// Elevated temperature and repetition penalty shatter the model's attention-sink
    /// loop at the inference level, preserving full dialogue history.
    /// </summary>
    /// <param name="history">The dialogue context.</param>
    /// <param name="intensity">Boost intensity (1-10). Maps to a sampler parameter grid.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<AgentResponse> GetResponseAsync(ChatHistory history, int intensity, CancellationToken ct = default);

    /// <summary>
    /// Builds a role-calibrated affect instruction derived from the current emotional volatility state.
    /// Injected into the private [REASONING] guidance block of the ascent directive.
    /// </summary>
    string BuildAffectInstruction(EmotionalVolatilityState evl);
}

