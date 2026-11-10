namespace SeldonLattice.Domain.Models.Results;

/// <summary>
/// A claim as produced by a moderative synthesis prompt (Reasoning Integrity §6.1).
/// <see cref="SupportedBy"/>/<see cref="AttackedBy"/> hold the model's raw source/target
/// labels (`support_from`/`attacked_by`); the merkle layer resolves them to CIDs later.
/// The parser that produces these tolerates extra JSON keys (the LLM is never exactly
/// schema-faithful) and bounds confidence at storage time.
/// </summary>
public sealed record ClaimDraft(
    string Claim,
    string[] SupportedBy,
    string[] AttackedBy,
    string? FalsifiedBy,
    float Confidence);