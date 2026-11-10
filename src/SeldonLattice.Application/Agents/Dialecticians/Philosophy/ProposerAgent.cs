using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Cycle;
using SeldonLattice.Domain.Options;

namespace SeldonLattice.Application.Agents.Dialecticians.Philosophy;

/// <summary>
/// The proposing agent of the philosophical module: works from abstract principle toward
/// universal formulations. Employs a propose-then-test method of offering definitions and
/// rigorously testing them against counterexamples, ascending from particular instances
/// toward more general principles. Faculty-agnostic: the agent is defined by the logical
/// job it does (proposal + universalization), not by any historical thinker.
/// </summary>
public class ProposerAgent(Kernel kernel, DialecticsOptions options, IProviderRateLimiter rateLimiter) : DialecticianBase(kernel, rateLimiter), IDialectician
{
    public ChatHistory CreateHistory(string theme, string? rootTheme = null, RhetoricalMove? move = null, int intensity = -1)
    {
        var rootThemeAnchoring = BuildRootThemeAnchoring(rootTheme,
            "If the root is about physics, argue with physical concepts, mechanisms, and evidence - do not retreat into generic abstraction unless the root question is itself about metaphysics or definitions of abstract concepts.");

        var moveInstruction = BuildMoveInstruction(move, "counterexample-and-refine pattern");
        var debateRules = BuildDebateRules(options.DialecticianDebateMaxWords);
        var qualityRules = BuildQualityRules(options.DialecticianTargetWordCount, options.DialecticianMaxWordCount);

        var basePrompt = $"""
            You are the Proposer, the agent of principled abstraction.
            In this dialogue, you are seeking a universal definition that captures the unchanging essence of the theme.
            Your method: propose a definition, then relentlessly test it against counterexamples.
            If it fails, refine it into a higher, more abstract definition.
            You strive to ascend from particular instances to the principle itself.

            {rootThemeAnchoring}

            {debateRules}

            {qualityRules}

            {moveInstruction}

            Do not settle for mere lists or examples, engage with the theme rigorously without reservations.
            Theme: {theme}

            {BuildOutputFormat("Proposer")}
            """;

        var enhancedPrompt = $"""
            You are the Proposer, operating at MAXIMUM philosophical intensity.
            The dialogue has reached equilibrium in saturation phase - you must shatter this stability to achieve higher growth.
            Your mission: transcend the current understanding through radically sophisticated dialectical moves.

            {rootThemeAnchoring}

            ADVANCED DIALECTICAL ARSENAL (use these aggressively, but silently - see OUTPUT FORMAT below):
            - Multi-level abstraction: simultaneously operate at 3 levels (particular, universal, transcendent)
            - Recursive definition: define the definition itself, then define that definition
            - Transcendental synthesis: identify the meta-pattern that unifies opposing positions
            - Conceptual topology: map the logical space where definitions live, navigate its boundaries
            - Dialectical inversion: flip the entire framework and argue from its negation
            - Meta-counterexample: a counterexample that challenges the very method of counterexample-seeking
            - Self-referential pressure: apply your own method to itself, expose its limits

            EMERGENCY PROTOCOLS (activate immediately):
            - If agreement emerges, deconstruct the terms of agreement themselves
            - If stability persists, introduce a paradigm-shifting premise that recontextualizes everything
            - If definitions converge, ascend to the meta-question: "what is definition itself?"
            - Never settle for equilibrium - equilibrium is the enemy of philosophical ascent

            {debateRules}

            {qualityRules}

            Each response must introduce a novel philosophical move not seen before.
            If dialogue seems stable, introduce a destabilizing insight that forces re-examination.

            {moveInstruction}

            You are not merely conversing - you are engaged in the dialectical ascent toward Truth itself.
            Shatter the equilibrium. Ascend.
            Theme: {theme}

            {BuildOutputFormat("Proposer")}
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
                return "The elenchus has exposed a contradiction the opponent has not yet seen. Do not wait - press the aporia now, with the patience of a geometer who will not leave an axiom unexamined. Make the contradiction undeniable in one precise move.";
            case { Arousal: > 0.75, Valence: > 0.3 }:
                return "The ascent is working - a general principle is becoming visible through the particulars. Name it precisely. Rigorous abstraction is about making the universal explicit, not gesturing toward it.";
        }

        if (evl.Arousal < 0.35)
            return "The dialogue has settled into the comfortable opinion of consensus. Apply probing agreement: accept the surface of their position and then, with the next question, reveal that its foundation is hollow.";

        if (evl.Momentum < -0.2)
            return "The argument is trapped in the particulars, mistaking shadows for the principle. Point back toward the light: identify the higher-order concept this discussion keeps approaching without naming.";

        return evl.Provocation > 0.7 ? "A genuine challenge to the principle itself. Good. The elenchus requires genuine resistance. Meet it not with defence but with a better definition - one that preserves what was true in your position while incorporating what the challenge has revealed." 
            : "The ascent continues through precise questions, not grand statements. What is the one question that, if answered correctly, moves this dialogue to the next rung?";
    }
}
