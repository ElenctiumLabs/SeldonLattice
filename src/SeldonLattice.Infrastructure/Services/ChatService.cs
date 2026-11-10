using Microsoft.SemanticKernel;
using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.Infrastructure.Services;

/// <summary>
/// Simple chat service that executes arbitrary prompts through the Semantic Kernel LLM integration.
/// Provides a basic wrapper around kernel invocation for general-purpose LLM queries.
/// </summary>
public class ChatService(Kernel kernel) : IChatService
{
    /// <summary>
    /// Executes a prompt through the LLM and returns the response text.
    /// Falls back to an error message if the LLM service is unavailable.
    /// </summary>
    /// <param name="prompt">The prompt text to send to the language model.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>The LLM response as a string, or an error message on failure.</returns>
    public async Task<string> GetResponseAsync(string prompt, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await kernel.InvokePromptAsync(prompt, cancellationToken: cancellationToken);
            return result.GetValue<string>() ?? string.Empty;
        }
        catch (Exception ex)
        {
            return $"The LLM service is currently unavailable. {ex.Message}";
        }
    }
}
