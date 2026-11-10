namespace SeldonLattice.Infrastructure.Services;

/// <summary>
/// Validation applied to embedding vectors before they reach the metric stack.
///
/// The scoring geometry is entirely cosine-based, so a vector that is the wrong dimension,
/// the wrong scale, or from a different vector space yields plausible numbers and wrong
/// scores. These checks turn that silent failure into a loud one.
///
/// Read from configuration section "Embeddings:Validation".
/// </summary>
public class EmbeddingValidationOptions
{
    /// <summary>Enable the shape checks (dimension + L2 norm). Default true.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Expected vector dimension. 0 disables the dimension check. The stored corpus and the
    /// embedding model must agree; nomic-embed-text-v1.5 is 768.
    /// </summary>
    public int ExpectedDimension { get; init; } = 768;

    /// <summary>
    /// Expected L2 norm. Embeddings are L2-normalised by convention here
    /// (llama.cpp <c>--embd-normalize 2</c>), and every cosine assumes it.
    /// Set 0 to disable the norm check.
    /// </summary>
    public float ExpectedL2Norm { get; init; } = 1.0f;

    /// <summary>Tolerance for the L2 norm check. Default 0.01.</summary>
    public float NormTolerance { get; init; } = 0.01f;

    /// <summary>
    /// When true, a failed shape check throws instead of logging. Use in CI and during a
    /// provider migration; leave false in production so a bad vector degrades rather than
    /// taking the session down.
    /// </summary>
    public bool ThrowOnShapeFailure { get; init; }

    // -- Continuity ----------------------------------------------------------

    /// <summary>
    /// A fixed string embedded at startup and compared against the reference vector to prove
    /// the server is still in the same vector space.
    ///
    /// The reference must describe the space the STORED CORPUS lives in - not the space of
    /// whatever server is currently running. A reference captured from the live server would
    /// compare the space against itself and pass trivially. The practical choice is the exact
    /// text of an existing stored node, whose embedding is already in the store.
    /// </summary>
    public string SentinelText { get; init; } = "continuity sentinel: the dialectical method";

    /// <summary>
    /// Path to a JSON file holding the known-good sentinel vector (a flat array of floats).
    /// Preferred over <see cref="SentinelReference"/>: a 768-dim vector is ~9 KB, which is
    /// unpleasant inline and easy to mangle by hand. Relative paths resolve against the
    /// content root.
    /// </summary>
    public string? SentinelReferencePath { get; init; }

    /// <summary>
    /// The known-good vector for <see cref="SentinelText"/>, inline. Use only for tests or
    /// very small dimensions; prefer <see cref="SentinelReferencePath"/>.
    /// </summary>
    public float[]? SentinelReference { get; init; }

    /// <summary>
    /// Minimum cosine between the sentinel's live embedding and its reference. Below this the
    /// vector space has changed and stored vectors are no longer comparable with new ones.
    /// Default 0.99.
    /// </summary>
    public float MinSentinelCosine { get; init; } = 0.99f;

    /// <summary>
    /// When true a failed continuity check throws. This is the check that catches a provider
    /// migration silently changing the space, so it should be true wherever the corpus must
    /// stay comparable.
    /// </summary>
    public bool ThrowOnContinuityFailure { get; set; } = true;
}
