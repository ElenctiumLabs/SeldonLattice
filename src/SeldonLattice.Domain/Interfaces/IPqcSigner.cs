using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Domain.Interfaces;

/// <summary>
/// Post-quantum digital signature service (NIST ML-DSA standard).
/// Signs arbitrary content with a per-signer key and verifies signatures.
/// The signing key for a signer identity is stable: repeated calls for the
/// same <see cref="SignerId"/> sign with the same key material.
/// </summary>
public interface IPqcSigner
{
    /// <summary>
    /// Signs the given content on behalf of the signer.
    /// </summary>
    /// <param name="signerId">Identity that signs (agent, system, user).</param>
    /// <param name="content">The content bytes to sign.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A self-verifying signature containing the public key.</returns>
    Task<AgentSignature> SignAsync(string signerId, byte[] content, CancellationToken ct = default);

    /// <summary>
    /// Verifies a signature against content using the signature's embedded public key.
    /// </summary>
    /// <param name="signature">The signature produced by <see cref="SignAsync"/>.</param>
    /// <param name="content">The content bytes that were signed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when the signature is valid for the content.</returns>
    Task<bool> VerifyAsync(AgentSignature signature, byte[] content, CancellationToken ct = default);
}