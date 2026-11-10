namespace SeldonLattice.Domain.Models.Cycle;

/// <summary>
/// Reasons for dialogue termination by the quality gates system.
/// </summary>
public enum DialogueExitReason
{
    /// <summary>Maximum turn count exceeded.</summary>
    MaxTurnsExceeded,

    /// <summary>Farewell detected 3+ consecutive times.</summary>
    EndlessGoodbye,

    /// <summary>Response similarity score too high.</summary>
    RepetitiveContent,

    /// <summary>Both agents agree for N+ turns with no new ideas.</summary>
    AgreementWithoutTension,

    /// <summary>Quality score below threshold for consecutive turns.</summary>
    QualityScoreDegraded,

    /// <summary>Natural conclusion reached (agent-initiated).</summary>
    SynthesisReached,

    /// <summary>Irreducible tension identified (agent-initiated).</summary>
    AporeaReached,

    /// <summary>Human user requested termination.</summary>
    UserRequested,

    /// <summary>Prompt injection or security threat detected.</summary>
    PromptInjectionDetected,

    /// <summary>Responses drifted semantically off the root theme for consecutive turns.</summary>
    OffTopicDrift
}