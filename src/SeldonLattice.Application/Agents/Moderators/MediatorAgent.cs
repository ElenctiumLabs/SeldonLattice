using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Application.Extensions;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Agents;
using SeldonLattice.Domain.Models.Cycle;
using SeldonLattice.Domain.Models.Results;
using SeldonLattice.Domain.Options;

namespace SeldonLattice.Application.Agents.Moderators;

/// <summary>
/// Mediating moderator and stalemate-breaker.
/// As <see cref="IDialecticalModerator"/>: synthesizes metaphors, proposes next themes,
/// and gates ascension.
/// As <see cref="IMediator"/>: generates targeted interjections to break stalemates.
///
/// Agent display names are injected at construction so the prompts correctly name
/// whichever dialecticians are currently debating rather than hardcoding role labels.
/// </summary>
public class MediatorAgent(
    Kernel kernel,
    IOptions<DialecticsOptions> options,
    IProviderRateLimiter rateLimiter,
    string dialectician1Name,
    string dialectician2Name,
    string dialogueLabel) : ModeratorBase(kernel, options, rateLimiter), IDialecticalModerator, IMediator
{
    private readonly DialecticsOptions _options = options.Value;

    // -- Fallback (ModeratorBase contract) ------------------------------------

    protected override string FallbackImageDomain => "dialectic";

    protected override MetaphorResult FallbackResult => new(
        string.Empty, FallbackImageDomain, string.Empty);

    // -- Prompt templates ------------------------------------------------------

    private string MetaphorPrompt => $$"""
        You are the Mediator, the midwife of ideas, synthesizing a genuine philosophical
        resolution - not decorating the debate with mood-poetry.

        {{dialectician1Name}} and {{dialectician2Name}} have reached a balanced understanding on the theme "{0}".
        The dialogue is currently in a phase of {1}.
        {{dialectician1Name}}'s final insight: {2}
        {{dialectician2Name}}'s final insight: {3}

        FORBIDDEN IMAGE DOMAINS (already used earlier this session - do not reuse or riff
        on them in any form, including close cousins of them): {4}

        Your task has two parts:
        1. Identify the actual philosophical move being made: not "both sides have a point,"
           but the specific way {{dialectician1Name}}'s abstraction and {{dialectician2Name}}'s 
           grounding resolve into a claim neither held alone - a genuine synthesis, 
           a distinction that dissolves the apparent conflict, or a reframing that shows
           the conflict was about two different senses of the same term. 
           Name this precisely to yourself before writing anything.
        2. Render that specific move as a metaphor drawn from a domain NOT in the forbidden
           list above (examples: mathematics, craft or toolmaking, navigation, architecture, biology,
           aesthetics, art theory, music theory, cartography, chemistry, law, games and strategy, 
           weather systems, geometry, creative writing, astronomy, textile work, cooking, cartography, 
           medicine and healing - pick whichever domain actually fits the move; don't force one that doesn't).

        Output exactly five parts, each starting on its own line, nothing before or after:
        DOMAIN: <one or two words naming the image domain you used, e.g. "clockwork" or "cartography" or "healing">
        THESIS: <one plain sentence stating the actual philosophical claim being synthesized>
        REMAINING DISSENSUS: <the sharpest disagreement that remains unresolved, or "None" if none remains>
        REPEATS PREMISE: <a number from 0 to 1 estimating how much the synthesis merely restates the original question>
        METAPHOR: <the story itself, prose, no labels or headers inside it>
        """;

    private string ThemePrompt => $$"""
        Propose the next concrete question in this {{dialogueLabel}} inquiry.

        Root question: "{2}"
        Current question: "{0}"

        Rungs already climbed in this inquiry:
        {3}

        The debate at the current level produced this thesis: {4}
        Synthesis: {1}

        Ask one concise, answerable question that develops the synthesis and helps answer the
        root question at a broader level. It must:
        - open a conceptual dimension not already covered by the rungs above;
        - preserve a recognisable connection to the root question.

        Do not repeat the current question or any earlier rung. Do not invent a connection based
        only on shared metaphor imagery.

        If no warranted new dimension is available from this synthesis - if every question you
        could ask here would only restate a rung already climbed - do not invent one. Decline:

        EXHAUSTED: <one sentence explaining why the inquiry is spent at this rung>

        Otherwise return exactly these two lines:
        QUESTION: <one question>
        GENERALIZES: <one sentence explaining the connection to the root>
        """;

    private const string AscensionGatePrompt = """
        You are the Mediator, gatekeeper of the dialectical ladder.

        Root question (the bottom of the ladder): {0}
        Proposed next question (the candidate next rung): {1}
        Claimed connection: {2}

        Rungs already climbed in this inquiry:
        {3}

        Judge strictly on two counts:
        1. Root connection: would answering the proposed question, in principle, tell you
           something about how to answer the root question? Or has the inquiry drifted onto a
           different topic that merely shares vocabulary or imagery with the root?
        2. Distinction: is the proposed question a genuine new rung, or does it restate - in new
           words - a rung already climbed above? A semantic restatement counts as a repeat, not a
           new rung, even when no word is shared.

        Respond with a single JSON object and nothing else - no markdown code fences, no text
        before or after it:
        {{"valid": true or false, "reasoning": "one plain sentence explaining your judgment"}}
        """;

    private const string ExhaustionGatePrompt = """
        You are the Mediator, gatekeeper of the dialectical ladder.

        Root question (the bottom of the ladder): {0}
        The moderator has declined to propose the next rung. Its reason: {1}

        Rungs already climbed in this inquiry:
        {2}

        Judge strictly: is the claim that this inquiry is spent at this point warranted, given the
        rungs already climbed and the root question? Or is the moderator declining because finding
        the next rung is harder than saying there is none?

        Answer true only when the inquiry has genuinely reached its limit here - when any further
        question would restate a rung already climbed rather than open a new one.

        Respond with a single JSON object and nothing else - no markdown code fences, no text
        before or after it:
        {{"valid": true or false, "reasoning": "one plain sentence explaining your judgment"}}
        """;

    // -- IDialecticalModerator --------------------------------------------------

    /// <inheritdoc/>
    public async Task<MetaphorResult> SynthesizeMetaphorAsync(
        string theme,
        string dialectician1Reply,
        string dialectician2Reply,
        string phase,
        IReadOnlyList<string> forbiddenDomains,
        CancellationToken ct = default,
        int maxRetries = 2)
    {
        var forbiddenList = forbiddenDomains.Count > 0 ? string.Join(", ", forbiddenDomains) : "(none yet)";
        var prompt = string.Format(MetaphorPrompt, theme, phase, dialectician1Reply, dialectician2Reply, forbiddenList);

        MetaphorResult result;
        var attempt = 0;

        do
        {
            attempt++;
            var raw = await CallLlmAsync(prompt, ct);
            result = ParseTextSynthesisResponse(raw.Trim());

            var domainRepeated = !string.IsNullOrWhiteSpace(result.ImageDomain)
                && forbiddenDomains.Any(d => d.Equals(result.ImageDomain, StringComparison.OrdinalIgnoreCase));
            var incomplete = string.IsNullOrWhiteSpace(result.ImageDomain)
                || string.IsNullOrWhiteSpace(result.Thesis)
                || string.IsNullOrWhiteSpace(result.Metaphor);

            if (!domainRepeated && !incomplete)
                break;

            if (incomplete)
            {
                // The reply dropped one or more labels. Restate the contract rather than
                // keeping a partial parse (or the raw reply) as the synthesis.
                prompt += "\n\nYour previous reply was missing one or more of the required " +
                          "DOMAIN, THESIS or METAPHOR labels. Output exactly the five labelled " +
                          "lines, and make sure the METAPHOR label is present.";
            }
            else
            {
                prompt += $"\n\nYour previous attempt used the domain \"{result.ImageDomain}\", which is " +
                          "on the forbidden list above. Choose a genuinely different domain this time.";
            }
        }
        while (attempt <= maxRetries);

        return result;
    }

    /// <inheritdoc/>
    public async Task<NextThemeResult> SynthesizeNextThemeAsync(
        string metaphor,
        string currentTheme,
        string rootTheme,
        string? priorRejectionFeedback = null,
        IReadOnlyList<string>? climbedThemes = null,
        string? synthesisThesis = null,
        CancellationToken ct = default)
    {
        var prompt = string.Format(
            ThemePrompt,
            currentTheme,
            metaphor,
            rootTheme,
            FormatClimbedThemes(climbedThemes),
            string.IsNullOrWhiteSpace(synthesisThesis) ? "(no thesis was recorded)" : synthesisThesis);
        if (!string.IsNullOrWhiteSpace(priorRejectionFeedback))
            prompt += $"\n\nA previous attempt at this was rejected: \"{priorRejectionFeedback}\". " +
                      "Produce a different question that avoids that problem.";

        var raw = await CallLlmAsync(prompt, ct);
        var parsed = ParseNextThemeResult(raw);
        // A decline is a complete answer, not a failed parse: it must not trigger the
        // question-recovery path, which would discard the moderator's reason.
        if (parsed.IsExhausted || !string.IsNullOrWhiteSpace(parsed.Theme)) return parsed;

        // Small local models can follow the explanation request but omit the actual
        // question. Recover once with a minimal, explicitly labelled response contract.
        var recoveryFeedback = string.IsNullOrWhiteSpace(priorRejectionFeedback)
            ? string.Empty
            : $"Correction: {priorRejectionFeedback}";
        var recoveryPrompt = $$"""
            Write one concrete next question that is related to the root question and develops
            the synthesis. Do not repeat the current question or any earlier rung.
            Root: {{rootTheme}}
            Current: {{currentTheme}}
            Rungs already climbed:
            {{FormatClimbedThemes(climbedThemes)}}
            Thesis: {{(string.IsNullOrWhiteSpace(synthesisThesis) ? "(no thesis was recorded)" : synthesisThesis)}}
            Synthesis: {{metaphor}}
            {{recoveryFeedback}}
            Output exactly:
            QUESTION: <one question>
            GENERALIZES: <one sentence connecting it to the root>
            Or, if the inquiry is genuinely spent at this rung, decline with:
            EXHAUSTED: <one sentence explaining why>
            """;
        var recoveryRaw = await CallLlmAsync(recoveryPrompt, ct);
        return ParseNextThemeResult(recoveryRaw);
    }

    /// <summary>
    /// Renders the ladder for the next-theme prompt: one numbered rung per line, oldest first.
    /// An empty ladder renders as an explicit placeholder so the model is never faced with a
    /// blank section it might read as an instruction.
    /// </summary>
    internal static string FormatClimbedThemes(IReadOnlyList<string>? climbedThemes)
        => climbedThemes is { Count: > 0 }
            ? string.Join("\n", climbedThemes.Select((theme, i) => $"Rung {i + 1}: {theme}"))
            : "(none yet)";

    /// <inheritdoc/>
    public async Task<AscensionVerdict> EvaluateAscensionAsync(
        string rootTheme,
        string candidateTheme,
        string connectiveSentence,
        IReadOnlyList<string>? climbedThemes = null,
        CancellationToken ct = default)
    {
        var prompt = string.Format(
            AscensionGatePrompt,
            rootTheme,
            candidateTheme,
            string.IsNullOrWhiteSpace(connectiveSentence) ? "(none given)" : connectiveSentence,
            FormatClimbedThemes(climbedThemes));

        var raw = await CallLlmAsync(prompt, ct);
        return ParseAscensionVerdict(raw);
    }

    /// <inheritdoc/>
    public async Task<AscensionVerdict> EvaluateExhaustionAsync(
        string rootTheme,
        string exhaustionReason,
        IReadOnlyList<string>? climbedThemes = null,
        CancellationToken ct = default)
    {
        var prompt = string.Format(
            ExhaustionGatePrompt,
            rootTheme,
            string.IsNullOrWhiteSpace(exhaustionReason) ? "(no reason given)" : exhaustionReason,
            FormatClimbedThemes(climbedThemes));

        var raw = await CallLlmAsync(prompt, ct);
        return ParseAscensionVerdict(raw);
    }

    // -- IModerator: CreateSynthesisHistory ------------------------------------

    public override ChatHistory CreateSynthesisHistory(
        string theme,
        AgentResponse[] previousResponses,
        string moderationFocus,
        string cyclePhase = "growth")
    {
        var prompt = $"""
            You are the Mediator, the midwife of ideas, synthesizing a genuine philosophical
            resolution from the debate between {dialectician1Name} and {dialectician2Name}.

            Theme: {theme}
            Dialogue phase: {cyclePhase}

            {dialectician1Name}'s final insight: {previousResponses.ElementAtOrDefault(0)?.Content ?? "(none)"}
            {dialectician2Name}'s final insight: {previousResponses.ElementAtOrDefault(1)?.Content ?? "(none)"}

            Moderation focus: {moderationFocus}

            OUTPUT: JSON with exactly these fields (no markdown, no code fences):
            """ + """

            {"image_domain": "<one or two words naming the metaphor's domain>",
             "metaphor": "<the metaphor story, prose>",
             "thesis": "<one plain-sentence claim>",
             "next_theme": "<one probing question for the next rung>",
             "connective_sentence": "<how it connects to the root question>"}
            """;

        return new ChatHistory(prompt);
    }

    // -- IMediator: AnalyzeTensionAsync ----------------------------------------

    /// <inheritdoc/>
    public async Task<MediationContext> AnalyzeTensionAsync(
        AgentResponse dialectician1Response,
        AgentResponse dialectician2Response,
        string theme,
        ChatHistory dialectician1History,
        ChatHistory dialectician2History,
        CancellationToken ct = default)
    {
        var history1 = dialectician1History.FormatForMediator();
        var history2 = dialectician2History.FormatForMediator();

        var prompt = $$"""
            You are the Mediator, observing {{dialectician1Name}} and {{dialectician2Name}} debate "{{theme}}".

            {{dialectician1Name}}'s position: {{dialectician1Response.Content}}
            {{dialectician2Name}}'s position: {{dialectician2Response.Content}}

            Conversation history of {{dialectician1Name}} (most recent last):
            {{history1}}

            Conversation history of {{dialectician2Name}} (most recent last):
            {{history2}}

            Analyze the tension. What unspoken assumption underlies both positions?
            What third option exists beyond this polarity?

            OUTPUT: JSON with exactly these fields (no markdown, no code blocks):
            {"mediator_insight": "bridging analysis", "tensions": ["tension1", "tension2"], "resolution": "path forward"}
            """;

        var raw = await CallLlmAsync(prompt, ct);

        var startIdx = raw.IndexOf('{');
        var endIdx = raw.LastIndexOf('}');
        if (startIdx >= 0 && endIdx > startIdx)
            raw = raw[startIdx..(endIdx + 1)];

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var insight = root.TryGetProperty("mediator_insight", out var m) ? m.GetString() ?? "bridging mediation" : "bridging mediation";
            var tensions = root.TryGetProperty("tensions", out var t)
                ? t.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToArray()
                : ["unexamined_assumption"];
            return new MediationContext(insight, string.Empty, _options.DefaultTensionIntensity, tensions);
        }
        catch
        {
            return new MediationContext(raw.Length > 0 ? raw : "bridging insight", string.Empty, _options.DefaultTensionIntensity, ["unexamined_assumption"]);
        }
    }

    // -- Interjection system ----------------------------------------------------

    private static readonly Dictionary<StalemateSignal, string[]> InterjectionDevices = new()
    {
        [StalemateSignal.CircularRestatement] =
        [
            "Demand each participant argue the OTHER's position for one turn - a role-swap.",
            "Ask each to state, in exactly one sentence, what has actually changed between their last two turns - and note aloud if the honest answer is 'nothing.'",
            "Require both to abandon their current vocabulary entirely and restate the disagreement using only concrete, non-technical language.",
        ],
        [StalemateSignal.TalkingPast] =
        [
            "Force both to first define the central disputed term before saying anything else, and state the exact term they must define.",
            "Ask each to restate the OTHER's position in one sentence before responding to it - and note if they cannot.",
            "Identify a word each of them is using in a different sense, name both senses precisely, and ask which sense the debate is actually about.",
        ],
        [StalemateSignal.EntenchedNoMovement] =
        [
            "Take each view to its most extreme logical conclusion and ask what world that extreme would produce.",
            "Question whether the dispute is really about the stated topic at all, or whether it is a disagreement over the MEANING of one key term - name the term and ask each to define it without using the other's vocabulary.",
            "Point out a third position neither has stated, sketch it in one sentence, and demand each argue against IT instead of each other for one turn.",
            "Ask each what single observation or experience would change their mind - and note if they cannot name one, that is itself the problem.",
            "Invert the question: ask what would have to be true about the world itself (not about their philosophy) for both positions to be simultaneously correct.",
        ],
        [StalemateSignal.PremiseLevelFork] =
        [
            "Name the single deepest premise where they diverge in one clause, and tell each to defend ONLY that premise, not the downstream conclusions.",
            "Ask each to state the premise they think the OTHER is unwilling to give up, and why.",
            "Propose a scenario where the disputed premise is simply false, and ask each what would remain of their position.",
        ],
        [StalemateSignal.MissingInformation] =
        [
            "Inject one concrete historical, empirical, or observable fact directly relevant to the debate (specific: name, date, place, or measurement) and state what question it forces them to answer.",
            "Ask what single piece of evidence, if it existed, would settle the dispute - and whether either has actually looked for it.",
            "Point out a real-world case that neither has mentioned, and ask each to explain it under their own view before continuing.",
        ],
        [StalemateSignal.AgreementStagnation] =
        [
            "Congratulate them briefly, then immediately identify the hardest edge case or counterexample that could break their new consensus, posed as a direct question.",
            "Ask each to argue against the agreement they just reached, as if playing devil's advocate for one turn.",
            "Point out a consequence of their agreement neither has stated yet, and ask whether they actually accept it.",
        ],
    };

    private readonly Dictionary<StalemateSignal, int> _deviceCursor = new();

    /// <inheritdoc/>
    /// The Mediator overrides <see cref="BuildInterjectionPrompt"/> to cycle through a library
    /// of specific rhetorical devices per signal type, producing more varied and targeted
    /// interventions than the generic base implementation.
    protected override string BuildInterjectionPrompt(
        StalemateSignal signal,
        string theme,
        string d1Snippet,
        string d2Snippet,
        string d1Name,
        string d2Name)
    {
        var diagnosis = signal switch
        {
            StalemateSignal.CircularRestatement  => $"{d1Name} and {d2Name} have begun to restate themselves in circles.",
            StalemateSignal.TalkingPast          => $"{d1Name} and {d2Name} are talking past each other, not engaging one another's actual claim.",
            StalemateSignal.EntenchedNoMovement  => $"{d1Name} and {d2Name} are entrenched with no movement between turns.",
            StalemateSignal.PremiseLevelFork     => $"{d1Name} and {d2Name} disagree at the level of root premises, not conclusions.",
            StalemateSignal.MissingInformation   => $"{d1Name} and {d2Name} appear to be blocked by missing information.",
            StalemateSignal.AgreementStagnation  => $"{d1Name} and {d2Name} have settled into agreement too quickly, without testing it.",
            _                                    => $"{d1Name} and {d2Name} have reached a stalemate.",
        };

        var devices = InterjectionDevices[signal];
        var next = _deviceCursor.TryGetValue(signal, out var last) ? (last + 1) % devices.Length : 0;
        _deviceCursor[signal] = next;

        return string.Format($$"""
            You are the Mediator, the midwife of ideas, watching {{d1Name}} and {{d2Name}} debate "{0}".
            The pattern you observe: {3}
            {{d1Name}}'s position: {1}
            {{d2Name}}'s position: {2}

            Your move this time: {4}

            Write 2-3 sentences. Do not open with "your disagreement is real but unproductive"
            or any close variant of it - vary your opening every time you speak. Your
            interjection should not just referee the existing tension; it should surface
            something neither side has said yet - an unexamined term, a hidden third
            option, a question about the question itself. Aim for the kind of remark that
            would make a reader stop and reconsider the whole frame of the debate, not just
            restate that the two of them disagree.
            Write only the interjection - no preamble, no sign-off, no bracketed labels or headers.
            """, theme, d1Snippet, d2Snippet, diagnosis, devices[next]);
    }
}
