using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.API.Tests;

/// <summary>
/// Minimal IChatService stub returning a canned, JSON-shaped response so
/// dialectical ascent completes deterministically in integration tests.
/// </summary>
internal sealed class StubChatService : IChatService
{
    public Task<string> GetResponseAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var json = prompt.Contains("SYNTHESIS", StringComparison.OrdinalIgnoreCase)
            ? "{\"metaphor\":\"test synthesis\",\"reasoning\":\"stubbed\",\"response\":\"A tested synthesis.\",\"nextTheme\":\"next theme\"}"
            : "{\"reasoning\":\"stubbed reasoning\",\"response\":\"A stubbed dialectical response.\"}";
        return Task.FromResult(json);
    }
}