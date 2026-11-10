namespace SeldonLattice.Domain.Models.Agents;

/// <summary>
/// The result of a philosopher agent's turn, split into the clean spoken response
/// (the only thing ever streamed to the client or added back into ChatHistory) and
/// an optional private reasoning scratchpad. See <see cref="AgentResponseParser"/>
/// for why this split exists.
/// </summary>
public record AgentResponse(string Content, string? Reasoning = null, long? InputTokens = null, long? OutputTokens = null, string? ModelId = null);