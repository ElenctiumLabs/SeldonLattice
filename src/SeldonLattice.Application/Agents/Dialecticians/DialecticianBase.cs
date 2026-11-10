using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Agents;
using SeldonLattice.Domain.Models.Cycle;

namespace SeldonLattice.Application.Agents.Dialecticians;


/// <summary>
/// Abstract base for all dialectician agents. Extracts the shared prompt-construction
/// scaffolding - root-theme anchoring, move injection, debate rules, quality rules,
/// output format, and GetResponseAsync - so each concrete agent only specifies its
/// own role, concepts, method, and vocabulary.
/// </summary>
public abstract class DialecticianBase(Kernel kernel, IProviderRateLimiter rateLimiter)
{
    /// <summary>
    /// Creates the ChatHistory with the effective system prompt built by the subclass.
    /// The <see cref="PromptContracts.LabeledResponseContract"/> is placed at the very top
    /// of every dialectician system prompt so the model knows - before any role text -
    /// that its reply is machine-parsed into [REASONING]/[RESPONSE] sections and how the
    /// parser behaves when sections are missing. Skipped entirely when
    /// <see cref="PromptContracts.Enabled"/> is false (small local models ignore it).
    /// </summary>
    protected static ChatHistory CreateChatHistory(string systemPrompt) =>
        new(PromptContracts.Enabled
            ? $"{PromptContracts.LabeledResponseContract}\n\n{systemPrompt}"
            : systemPrompt);

    /// <summary>
    /// Generates the root-theme anchoring paragraph. Returns empty string when
    /// <paramref name="rootTheme"/> is null or whitespace.
    /// </summary>
    protected static string BuildRootThemeAnchoring(string? rootTheme, string domainFallback)
    {
        if (string.IsNullOrWhiteSpace(rootTheme))
            return string.Empty;

        return $"""
            This inquiry is one rung of a ladder whose root question is: {rootTheme}
            You are evaluating the theme being debated, not the root itself. Stay inside that
            root question's subject matter and vocabulary. {domainFallback}
            If this theme cannot be linked back to the root within one paragraph, say so
            explicitly instead of drifting.
            """;
    }

    /// <summary>
    /// Generates an optional-move suggestion paragraph. Returns empty string when
    /// <paramref name="move"/> is null.
    /// </summary>
    protected static string BuildMoveInstruction(RhetoricalMove? move, string defaultMethodName)
    {
        return !move.HasValue ? string.Empty : $"""
                                                OPTIONAL ANGLE TO CONSIDER: {MoveRotation.GetInstruction(move.Value)}
                                                Use this only if it is grounded and would help the inquiry. If it does not
                                                fit, continue with your {defaultMethodName} or state what remains unresolved;
                                                do not manufacture novelty to satisfy a move.
                                                """;
    }

    /// <summary>
    /// Returns the universal debate-rules block that every dialectician receives.
    /// These are the adversarial engagement rules: direct attack, framework opposition,
    /// no summarising, and the configurable word cap.
    /// </summary>
    protected static string BuildDebateRules(int maxWords) => $"""
        RULES:
        1. DO NOT summarize, paraphrase, or agree with the opposing speaker.
        2. Direct Attack: Your very first sentence must directly challenge or expose
           a fundamental flaw in the opposing speaker's premise. NEVER open with conversational
           concessions, throat-clearing, or polite praise (e.g. "While [Speaker] raises a valid point...",
           "While I respect...", "Indeed..."). Start IMMEDIATELY with the refutation.
        3. Bring your distinct philosophical framework to bear on every claim.
        4. DO NOT conclude by restating or summarizing the inquiry theme.
        5. Keep your response concise (under {maxWords} words).
        """;

    /// <summary>
    /// Returns the quality-enforcement rules shared by every dialectician.
    /// </summary>
    protected static string BuildQualityRules(int targetWords, int maxWords) => $"""
        CRITICAL QUALITY RULES:
        - NEVER use ceremonial language like "profound gratitude", "noble exchange",
          "deeply honored", "amen to that", or polite concessions like "While you raise valid points"
        - Prefer a grounded new counterexample, challenge, or refinement when it adds substance.
          Do not invent novelty to satisfy this preference; state what remains unresolved when
          no useful new angle is available.
        - Avoid reusing examples, analogies, historical cases, or arguments without adding context; refer back when needed for precision
        - NEVER simply agree or echo the other speaker's sentiments
        - AVOID repetitive phrases and farewell language
        - NEVER restate or summarize the prompt theme as a concluding sentence
        - NEVER re-introduce your name, role, or foundational opening thesis after Round 1
        - NEVER use bracketed labels (e.g. "[NEW PREMISE]"), bold headers
          (e.g. "**Refutation:**"), numbered meta-steps, or any commentary about your
          own reasoning process in what you say - speak only in character, in flowing
          prose. Any instructional scaffolding you're given (including this one) is for
          your eyes only and must never be echoed, labeled, or referenced in your reply.
        - TARGET {targetWords}-{maxWords} words per response - substantial and rigorous
        - MAX {maxWords + 100} WORDS - stop when the core argument is fully developed
        - Do not prolong the dialogue for its own sake. If it seems to be concluding, add a
          genuinely useful new angle only when one is grounded; otherwise identify the limit
          reached or the question that remains open.
        """;

    /// <summary>
    /// Returns the mandatory output-format block ([REASONING] / [RESPONSE]).
    /// </summary>
    protected static string BuildOutputFormat(string agentName) => $"""
        OUTPUT FORMAT (mandatory):
        [REASONING]
        your private notes only: the counterexample you're targeting, the refinement
        you're about to propose, which of the quality rules above you're applying, etc.
        This is never shown to anyone, so use it freely.

        [RESPONSE]
        what you actually say, entirely in character, as flowing prose only - no
        labels, no headers, no meta-commentary
        """;

    /// <summary>
    /// Returns a role-specific tonal calibration phrase derived from the agent's
    /// current <see cref="EmotionalVolatilityState"/>. This phrase is injected into
    /// the private <c>[REASONING]</c> guidance block of the ascent directive - it
    /// instructs the model on the emotional register appropriate to this role at
    /// this moment in the dialectic. It is NEVER part of the public <c>[RESPONSE]</c>.
    ///
    /// <para>
    /// Override in each concrete agent to map the four EVL dimensions (Arousal,
    /// Valence, Momentum, Provocation) to language authentic to that philosopher's
    /// temperament. The default implementation returns a generic activation phrase
    /// sufficient for agents that do not yet have a bespoke override.
    /// </para>
    /// </summary>
    public virtual string BuildAffectInstruction(EmotionalVolatilityState evl)
    {
        switch (evl)
        {
            case { Arousal: > 0.75, Valence: < -0.3 }:
                return "You are under genuine pressure. Let that pressure sharpen your argument - tighten your language, cut any softening qualifiers, and strike at the exact point where the opponent's case is weakest.";
            case { Arousal: > 0.75, Valence: > 0.3 }:
                return "Your argument is landing. Build on that momentum - press deeper into the territory you have opened, not wider across new ground.";
        }

        if (evl.Arousal < 0.35)
            return "The dialogue is losing heat. Introduce a genuinely destabilising move - something that reframes the stakes rather than advancing the same argument at lower volume.";

        if (evl.Momentum < -0.2)
            return "The trajectory is declining. Recover by going more specific, not more general - a precise, concrete case will do more than a broader claim.";

        return evl.Provocation > 0.7 ? "The opponent has struck a real blow at your core claim. Do not deflect. Address the hit directly, then turn it." 
            : "The dialectic is live. Stay sharp - every sentence should either advance or expand the central tension.";
    }

    /// <summary>
    /// Shared GetResponseAsync implementation. All dialecticians parse the LLM
    /// reply identically via <see cref="AgentResponseParser"/>.
    /// Enforces per-provider rate limits before each LLM call.
    /// </summary>
    public async Task<AgentResponse> GetResponseAsync(ChatHistory history, CancellationToken ct = default)
    {
        await rateLimiter.WaitForSlotAsync(ct);

        var chatCompletion = kernel.GetRequiredService<IChatCompletionService>();
        var response = await chatCompletion.GetChatMessageContentAsync(history, cancellationToken: ct);

        long? inputTokens = response.Metadata is not null && response.Metadata.TryGetValue("promptTokens", out var ptObj)
            ? Convert.ToInt64(ptObj) : null;
        long? outputTokens = response.Metadata is not null && response.Metadata.TryGetValue("completionTokens", out var ctObj)
            ? Convert.ToInt64(ctObj) : null;
                var parsed = AgentResponseParser.Parse(response.Content ?? string.Empty);
        return parsed with
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            ModelId = response.ModelId,
        };
    }

    /// <summary>
    /// Intensity-aware overload. Applies ascent pressure through raw sampler parameter
    /// adjustments instead of rebuilding the system prompt with extra tokens.
    /// This eliminates the Token Displacement Flaw: the full dialogue history is preserved
    /// in VRAM while the model's attention-sink loop is shattered via inference-level friction.
    ///
    /// Sampler boost grid (intensity 1–10):
    ///   Baseline (intensity ≤ 0):  temperature=default, top_p=default, repeat_penalty=default
    ///   Low      (intensity 1–4):  temperature=+0.2,    top_p=0.85,    repeat_penalty=+0.05
    ///   Medium   (intensity 5–9):  temperature=+0.4,    top_p=0.82,    repeat_penalty=+0.10
    ///   High     (intensity = 10): temperature=1.25,    top_p=0.80,    repeat_penalty=1.30
    /// </summary>
    public async Task<AgentResponse> GetResponseAsync(ChatHistory history, int intensity, CancellationToken ct = default)
    {
        await rateLimiter.WaitForSlotAsync(ct);

        // -- Zero-token sampler boost grid ----------------------------------------
        // Maps intensity to (temperature, top_p, repeat_penalty) without touching
        // the prompt. The OpenAICompatibleChatCompletionService reads these from ExtensionData.
        var (boostedTemp, boostedTopP, boostedRepeatPenalty) = intensity switch
        {
            >= 10 => (1.25, 0.80, 1.30),
            >= 5  => (0.90, 0.82, 1.25),
            >= 1  => (0.70, 0.85, 1.20),
            _     => ((double?)null, (double?)null, (double?)null),
        };

        PromptExecutionSettings? settings = null;
        if (boostedTemp is not null)
        {
            settings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    ["temperature"]    = boostedTemp.Value,
                    ["top_p"]          = boostedTopP!.Value,
                    ["repeat_penalty"] = boostedRepeatPenalty!.Value,
                }
            };
        }

        var chatCompletion = kernel.GetRequiredService<IChatCompletionService>();
        var response = await chatCompletion.GetChatMessageContentAsync(history, settings, cancellationToken: ct);

        long? inputTokens = response.Metadata is not null && response.Metadata.TryGetValue("promptTokens", out var ptObj)
            ? Convert.ToInt64(ptObj) : null;
        long? outputTokens = response.Metadata is not null && response.Metadata.TryGetValue("completionTokens", out var ctObj)
            ? Convert.ToInt64(ctObj) : null;
        var parsed = AgentResponseParser.Parse(response.Content ?? string.Empty);
        return parsed with
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            ModelId = response.ModelId,
        };
    }
}

