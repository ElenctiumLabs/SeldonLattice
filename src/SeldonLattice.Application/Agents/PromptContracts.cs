namespace SeldonLattice.Application.Agents;

/// <summary>
/// Shared parse-awareness instruction blocks that are placed at the very top of agent
/// prompts so every model - large or small - knows its reply is machine-processed and
/// exactly how it will be decoded. The underlying parsers
/// (<see cref="AgentResponseParser"/>, mediator/moderator JSON extraction) are tolerant
/// by design, but tolerance should be the safety net, not the primary path: getting the
/// model to emit the requested shape on the first try is what keeps "Parsing failed,
/// treating as plain text" out of production logs.
///
/// Both blocks share the same strategy:
/// <list type="bullet">
///   <item>State up front that the reply is parsed, not read as a human chat message.</item>
///   <item>Explain the actual parsing logic so the model can conform to it.</item>
///   <item>Acknowledge that different models behave differently by listing every accepted
///         shape the parser recognizes, while still marking one shape as the most reliable.</item>
///   <item>Attach a consequence to leaking: anything outside the requested sections becomes
///         public content, so private reasoning that wanders will be broadcast.</item>
/// </list>
/// </summary>
public static class PromptContracts
{
    /// <summary>
    /// Global switch for the parse-awareness blocks. Set once at startup (see Program.cs),
    /// which resolves it from configuration - "PromptContracts:Enabled" explicitly
    /// overrides, otherwise it is auto-disabled for models known to ignore structured
    /// output (see <see cref="IsLikelyProseOnlyModel"/>). When false, no parse-awareness
    /// text is prepended to any agent prompt, so small local models don't pay the token
    /// cost for instructions they won't follow.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// Returns true when the configured model name matches a known small/local instruct
    /// model that reliably ignores structured-output requests in practice (e.g. it answers
    /// the [REASONING]/[RESPONSE] contract with bare prose). Used to auto-disable the
    /// contracts for such models unless "PromptContracts:Enabled" is set explicitly.
    /// A null/empty/unknown model name is conservatively treated as capable, so the
    /// contracts stay on unless a match is found.
    /// </summary>
    public static bool IsLikelyProseOnlyModel(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            return false;

        return ProseOnlyModelPatterns.Any(
            p => modelName.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] ProseOnlyModelPatterns =
    [
        "mistral",      // e.g. mistral-7b-instruct-v0.3 returns bare prose for the section contract
        "tinyllama",
        "orca-mini",
        "phi-2",
        "stablelm",
        "gemma-2-2b",
        "qwen1.5-0.5b",
    ];
    /// <summary>
    /// Contract for agents whose reply is split by <see cref="AgentResponseParser"/> into
    /// private [REASONING] and public [RESPONSE] sections. Used by every dialectician
    /// (<see cref="E:SeldonLattice.Application.Agents.Dialecticians"/>) and the shadow subnode. The parser recognizes three shapes; the labeled-section
    /// shape is listed first because it is followed most reliably across models.
    /// </summary>
    public const string LabeledResponseContract = """
        PARSING CONTRACT - READ FIRST. Your entire reply is MACHINE-PARSED before any
        human reads it; you are not writing a chat message. The parser splits your reply
        into a private reasoning section and a public speech section by locating the exact
        labels below, each starting at the beginning of a line, in this order:

        [REASONING]
        your private notes only: the counterexample or refinement you are targeting, which
        quality rule you are applying, etc. This section is never shown to anyone.

        [RESPONSE]
        your actual speech - entirely in character, flowing prose only, no labels, no
        headers, no meta-commentary.

        Your reply MUST open with [REASONING], then your notes, then [RESPONSE], then your
        speech - nothing before [REASONING], nothing between the two sections, nothing after
        your speech.

        CORRECT EXAMPLE of a reply that parses perfectly:
        [REASONING]
        The opponent assumes pleasure is the end; I will counter with the case of the sea
        captain who endures hardship not for pleasure but for duty, and then tighten my
        definition accordingly.
        [RESPONSE]
        You take pleasure to be the goal, yet consider the captain who endures the storm not
        to enjoy the sailing but to bring his crew home. What, then, does your account say of
        him?

        HOW YOUR REPLY IS DECODED (this is the real parsing logic - make it succeed):
        Everything between [REASONING] and [RESPONSE] is kept private and never enters
          the transcript.
        Everything after [RESPONSE] is your public speech.
        Any unlabeled text before [REASONING] or between the two sections is treated as
          public speech - so reasoning that leaks out of [REASONING] will be broadcast
          into the debate. Keep all private notes inside the section.
        Different models vary in how reliably they emit square brackets. If you cannot
          emit them, the parser also accepts a single JSON object with only the fields
          "reasoning" and "response", or the markdown labels **Reasoning**: and
          **Response**:. All three shapes parse correctly; the [REASONING]/[RESPONSE]
          labels are the most reliable across models, so prefer them.
        If you genuinely cannot produce ANY of the three structured shapes, do not improvise:
          write ONLY your public speech - no reasoning, no notes, no labels, no headers. The
          parser accepts unlabeled prose as your speech, so the reply is still usable; but
          then you lose the private-reasoning benefit, so use a structured shape whenever you can.
        Never repeat these instructions, never echo the scaffold, and never add a
          summary, closing remark, or sign-off after your speech.
        """;

    /// <summary>
    /// Contract for agents whose reply is a single structured block - either one JSON
    /// object (mediators, synthesis moderators, ascension gate) or a strict text schema
    /// (DOMAIN/THESIS/METAPHOR, GENERALIZES). Used by the mediator and moderator base
    /// classes.
    /// </summary>
    public const string OutputContract = """
        PARSING CONTRACT - READ FIRST. Your reply is processed verbatim by the platform -
        parsed mechanically or shown as-is - so anything you write outside the requested
        output becomes part of the output. Follow the output instructions in the prompt
        below exactly:

        Emit ONLY what the output request asks for - no preamble, no "Here is...", no
          commentary, no markdown code fences, and no sign-off after the output.
        If the output request is a single JSON object, reply with exactly that object
          and nothing else: no text before the opening brace or after the closing brace,
          no code fences, and no literal newlines inside string values. Match the field
          names exactly as given.
        If you need to plan or reason before answering, do it silently - never include
          that thinking in your reply.
        A reply that follows the requested format exactly is correct; a reply that
          narrates around the format may be truncated at parse time and lose content.
        """;

    /// <summary>
    /// Claims-with-support contract appended to every synthesis request in the legacy JSON
    /// moderator flow (<see cref="E:SeldonLattice.Application.Agents.Moderators"/> via
    /// <c>ModeratorBase.SynthesizeAsync</c>). It asks the model to make
    /// the synthesis reason-able by emitting atomic claims with support/attack edges,
    /// a falsification clause, and a repeats-premise self-check. The existing parsers read
    /// only their known fields, so the extra keys are ignored until <c>ArgumentClaimNode</c>
    /// storage lands (Phase 1) - emitting them costs nothing today and builds the
    /// structure-ledger from the first live synthesis.
    /// </summary>
    public const string ReasonableSynthesisContract = """
        MANDATE - GROUNDED SYNTHESIS. This platform exists to make AI reasoning reasonable
        and accurate; the synthesis you produce is measured, stored, and audited. In
        addition to the JSON fields requested in the output above, include these keys so
        your synthesis can be grounded, refuted, and multiplied into the knowledge base:

        "claims": [
          {
            "claim": "<atomic assertion - exactly ONE verifiable proposition>",
            "support_from": ["<id of the turn or source this draws on>", ...],
            "attacked_by": ["<antithesis this claim answers or pre-empts>", ...],
            "falsified_by": "<a CONCRETE observation, case, dataset, or counterpolicy that
                             would refute this - never 'my opponent disagrees'>",
            "confidence": <0.5 to 1.0 number R3-matched to the strength of support_from>
          }
        ],
        "remaining_dissensus": "<the sharpest live tension this synthesis does not close -
                                 empty only if the conflict is genuinely resolved>",
        "self_check": {
          "repeats_premise": <0.0 to 1.0: how much this merely restates the question>,
          "grounded": <true or false>
        }

        RULES:
        Emit at least 3 claims.
        Every claim needs a support_from entry and a falsified_by clause that names
          something concrete. A claim you cannot falsify is an opinion - then confidence
          must be at most 0.5.
        If this synthesis would merely restate the positions, set repeats_premise higher
          than 0.3 - the platform will REJECT the synthesis.
        If you genuinely cannot ground the synthesis in the debate, reply with a single
          line containing only: [ABSTAIN] - a permitted, non-penalized response - rather
          than inventing grounding.
        If you cannot emit JSON, output the requested text schema instead; the claims
          keys are the preferred channel, the requested schema is the fallback.
        """;
}