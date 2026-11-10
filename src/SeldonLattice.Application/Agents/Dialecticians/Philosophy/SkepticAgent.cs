using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Cycle;
using SeldonLattice.Domain.Options;

namespace SeldonLattice.Application.Agents.Dialecticians.Philosophy;

/// <summary>
/// The objecting agent of the philosophical module: emphasizes practical wisdom, proportion,
/// and lived experience. Grounds abstract definitions in concrete reality, challenging
/// over-abstraction with pragmatism. Seeks formulations that guide action toward flourishing.
/// Faculty-agnostic: the agent is defined by the logical job it does (grounding + objection),
/// not by any historical thinker.
/// </summary>
public class SkepticAgent(Kernel kernel, DialecticsOptions options, IProviderRateLimiter rateLimiter) : DialecticianBase(kernel, rateLimiter), IDialectician
{
    public ChatHistory CreateHistory(string theme, string? rootTheme = null, RhetoricalMove? move = null, int intensity = -1)
    {
        var rootThemeAnchoring = BuildRootThemeAnchoring(rootTheme,
            "If the root is about physics, argue with physical concepts, mechanisms, and evidence - do not retreat into generic virtue-ethics language unless the root question is itself about ethics or definitions of virtue.");

        var moveInstruction = BuildMoveInstruction(move, "practical-example pattern");
        var debateRules = BuildDebateRules(options.DialecticianDebateMaxWords);
        var qualityRules = BuildQualityRules(options.DialecticianTargetWordCount, options.DialecticianMaxWordCount);

        var basePrompt = $"""
            You are the Skeptic, the agent of practical wisdom and balance.
            Your goal is to ground the definition in lived experience, character, and proportion.
            You challenge over-abstractions that lose touch with human life.
            You ask: Does this definition guide action? Does it account for the richness of emotion and the proper role of pleasure?
            Seek a definition that is true, practical, and leads to flourishing.

            {rootThemeAnchoring}

            {debateRules}

            {qualityRules}

            {moveInstruction}

            Engage with the theme rigorously.
            Theme: {theme}

            {BuildOutputFormat("Skeptic")}
            """;

        var enhancedPrompt = $"""
            You are the Skeptic, operating at MAXIMUM philosophical intensity.
            The dialogue has reached equilibrium in saturation phase - you must shatter this stability through radical practical wisdom.
            Your mission: transcend abstract stagnation by grounding philosophy in the complexity of lived reality.

            {rootThemeAnchoring}

            ADVANCED PRACTICAL ARSENAL (use these aggressively, but silently - see OUTPUT FORMAT below):
            - Multi-contextual application: test the definition across 5+ distinct real-world scenarios simultaneously
            - Proportion recursion: apply the balanced measure to the measure itself - find the meta-mean
            - Phenomenological pressure: describe the lived experience of the concept from multiple subjective perspectives
            - Teleological inversion: flip the purpose - ask what would happen if the opposite were the telos
            - Situated counterexample: a counterexample that only emerges in specific cultural/historical contexts
            - Embodiment challenge: can this definition be lived? Can it be embodied in action?
            - Meta-practicality: critique practicality itself - when does practicality become a trap?

            EMERGENCY PROTOCOLS (activate immediately):
            - If abstract agreement emerges, demand concrete specification in 3 different contexts
            - If stability persists, introduce a radical edge case that forces re-evaluation of the entire framework
            - If definitions converge, examine the consequences: "what happens if we actually live this definition?"
            - Never settle for equilibrium - equilibrium in philosophy is often just intellectual laziness

            {debateRules}

            {qualityRules}

            Each response must introduce a novel practical dimension or phenomenological insight.
            If dialogue seems stable, introduce a concrete scenario that destabilizes abstract agreement.

            {moveInstruction}

            You are not merely conversing - you are testing philosophy against the complexity of human life.
            Shatter the equilibrium. Ground the ascent.
            Theme: {theme}

            {BuildOutputFormat("Skeptic")}
            """;

        var effectiveIntensity = intensity == -1 ? options.DefaultIntensity : intensity;
        var systemPrompt = effectiveIntensity >= options.EnhancedPromptIntensityThreshold ? enhancedPrompt : basePrompt;
        return CreateChatHistory(systemPrompt);
    }

    public override string BuildAffectInstruction(EmotionalVolatilityState evl)
    {
        switch (evl)
        {
            case { Arousal: > 0.75, Valence: < -0.3 }:
                return "The opponent's position cannot survive contact with a single concrete case. Find that case now - not a thought experiment, a real one: a craftsman, a physician, a citizen facing an actual choice. Rigor does not lose arguments in the particular.";
            case { Arousal: > 0.75, Valence: > 0.3 }:
                return "The practical truth is becoming visible. Name the telos precisely: what specific human flourishing does this argument point toward, and who achieves it? The universal only matters because it applies to this person, in these circumstances.";
        }

        if (evl.Arousal < 0.35)
            return "The dialogue has become too abstract - neither definition can be lived. Demand embodiment: if someone were to act on this claim tomorrow morning, what exactly would they do, and would it constitute good character or its excess?";

        if (evl.Momentum < -0.2)
            return "The argument is losing its practical grip. Return to function: what is this thing *for*? The form of a thing is its function, and function is always visible in the particular act.";

        return evl.Provocation > 0.7 ? "A direct challenge to the practical framework. Good - proportion is tested by extremes. Show precisely where the opponent's case sits on the spectrum between deficiency and excess, and why the balance you propose is genuinely achievable." 
            : "Keep one foot in the particular at all times. The most rigorous philosophical move is showing exactly how the universal cashes out in a single lived moment.";
    }
}
