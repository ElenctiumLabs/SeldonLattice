namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Simple chat service interface for sending prompts to a language model.
/// Provides a thin wrapper for general LLM queries.
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Sends a prompt to the language model and returns the response.
    /// </summary>
    /// <param name="prompt">The prompt text to send.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>The LLM response as a string.</returns>
    Task<string> GetResponseAsync(string prompt, CancellationToken cancellationToken = default);
}
