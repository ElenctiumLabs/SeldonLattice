namespace SeldonLattice.Domain.Verification;

/// <summary>
/// Policy controlling how strict verification is.
/// <para>
/// The default is strict: every audit node must carry a signature. Session content nodes
/// written through the raw/legacy path are always reported as warnings. Under the permissive
/// policy even a missing audit signature is only a warning, so a session with no signatures
/// at all can still report success.
/// </para>
/// </summary>
public sealed record VerificationPolicy
{
    /// <summary>Strict: an unsigned audit node is an integrity failure.</summary>
    public static VerificationPolicy Strict { get; } = new();

    /// <summary>Permissive: an unsigned audit node is reported as a warning only.</summary>
    public static VerificationPolicy Permissive { get; } = new() { RequireSignatures = false };

    /// <summary>
    /// When true (the default), an audit node without a companion signature is an error.
    /// When false, it is reported as a warning. Session content nodes are never required to
    /// carry a signature, so their absence is always a warning.
    /// </summary>
    public bool RequireSignatures { get; init; } = true;

    /// <summary>
    /// Optional set of Base64-encoded ML-DSA public keys accepted as signers. When empty, any
    /// self-consistent signature verifies; when populated, a signature made with a key outside
    /// the set is an error.
    /// <para>
    /// This is what turns "the bytes have not changed" into "a known agent produced these
    /// bytes". Without it, an attacker who rewrites the store can re-sign the replacement with
    /// their own key and still produce a valid chain.
    /// </para>
    /// </summary>
    public IReadOnlySet<string> TrustedSignerPublicKeys { get; init; } = new HashSet<string>(StringComparer.Ordinal);
}
