namespace SeldonLattice.API.Authentication;

/// <summary>
/// Options for short-lived streaming tokens.
/// </summary>
public sealed class StreamTokenOptions
{
    /// <summary>HMAC signing secret. Must be set in configuration under "StreamTokens:Secret".</summary>
    public string Secret { get; init; } = "";

    /// <summary>Token lifetime in seconds (default 60).</summary>
    public int LifetimeSeconds { get; init; } = 60;
}