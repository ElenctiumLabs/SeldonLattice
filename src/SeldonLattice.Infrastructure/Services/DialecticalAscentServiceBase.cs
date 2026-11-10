using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Org.BouncyCastle.Security;
using SeldonLattice.Application.Extensions;
using SeldonLattice.Application.Memory;
using SeldonLattice.Application.Services;
using SeldonLattice.Domain.Configuration;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models;
using SeldonLattice.Domain.Models.Agents;
using SeldonLattice.Domain.Models.Cycle;
using SeldonLattice.Domain.Models.Results;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Domain.Options;
using SeldonLattice.Domain.Utilities;


namespace SeldonLattice.Infrastructure.Services;

/// <summary>
/// Base class for dialectical ascent services. Contains the shared streaming loop,
/// equilibrium detection, synthesis, and storage logic. Derived classes override
/// the hook methods to provide module-specific stalemate and mediation behavior.
/// </summary>
public abstract partial class DialecticalAscentServiceBase(
    ILogger logger,
    IMerkleStore merkleStore,
    ISessionService sessionService,
    Kernel kernel,
    DialecticsOptions options,
    CyclePhaseDetector phaseDetector,
    DialogueQualityGates qualityGates,
    IClaimMemoryRetriever? claimMemoryRetriever = null,
    LlmOptions? llmOptions = null) : IDialecticalAscentService
{
    private readonly int _stabilityWindow = options.StabilityWindow;
    private readonly int _maxTurns = options.MaxTurns;
    private readonly int _maxInterjections = options.MaxInterjections;
    private readonly float _variabilityMinimum = options.VariabilityMinimum;
    private readonly int _maxContextTurns = options.MaxContextTurns;
    private readonly int _maxAscentContextLevels = options.MaxAscentContextLevels > 0 ? options.MaxAscentContextLevels : int.MaxValue;

    protected DialecticalConfiguration _config = null!;
    private readonly LlmOptions _llmOptions = llmOptions ?? new();
    private string _ascentContextBlock = string.Empty;
    private string _claimMemoryBlock = string.Empty;
    private readonly HashSet<string> _claimMemoryInjectedSessions = [];
    private readonly HashSet<string> _moveSuggestionsIssued = new(StringComparer.Ordinal);

    private readonly List<float[]> _recentSynthesisEmbeddings = [];
    private readonly List<string> _usedMetaphorDomains = [];

    private int _interjectionCount;
    private int _saturationBoostCount;

    /// <summary>
    /// Mediation/interjection notes produced by derived hooks (Mediator interjections,
    /// per-turn mediation). These are folded into the next opponent-turn user message
    /// instead of being appended to the dialectician histories as standalone messages -
    /// a standalone user message would create consecutive user roles, which llama.cpp's
    /// chat template rejects ("Conversation roles must alternate..."), killing the
    /// stream right after the mediation event.
    /// </summary>
    private readonly Queue<string> _pendingMediatorNotes = [];

    /// <summary>
    /// Queues a mediator note to be surfaced to both dialecticians on their next turn.
    /// </summary>
    protected void InjectMediatorNote(string note)
    {
        if (!string.IsNullOrEmpty(note))
            _pendingMediatorNotes.Enqueue(note);
    }

    // State exposed to derived hooks during the streaming loop
    /** The most recent text response from Dialectician 1. Updated each turn. Available to derived class hooks. */
    protected string D1LastText { get; private set; } = string.Empty;
    /** The most recent text response from Dialectician 2. Updated each turn. Available to derived class hooks. */
    protected string D2LastText { get; private set; } = string.Empty;
    /** The full chat history for Dialectician 1, including system prompt, ascent context, and all turns. Available to derived class hooks. */
    protected ChatHistory D1History { get; private set; } = null!;
    /** The full chat history for Dialectician 2, including system prompt, ascent context, and all turns. Available to derived class hooks. */
    protected ChatHistory D2History { get; private set; } = null!;
    /**
     * The session's root question, resolved at the start of each level. Available to
     * derived class hooks (for example the fractal-echo boundary), which otherwise only
     * receive the current level's theme.
     */
    protected string RootTheme { get; private set; } = string.Empty;
    /** Accumulates formatted mediation insights from stalemate and per-turn mediation hooks. Passed to synthesis for context enrichment. */
    protected StringBuilder MediationInsights { get; } = new();

    /// <summary>
    /// Tensions that survived mediation, accumulated from each mediator's
    /// <see cref="MediationContext.UnresolvedTensions"/>. Populated by derived classes
    /// that run mediators (e.g. PhilosophyDialecticalAscentService).
    /// </summary>
    protected List<string> MediationUnresolvedTensions { get; } = [];

    /// <summary>
    /// The latest tension intensity reported by a mediator.
    /// </summary>
    protected float LatestTensionIntensity { get; set; }

    // -------------------------------------------------------------------------
    // Hooks - override in derived classes for module-specific behavior
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called when a stalemate is detected. Yield zero or more DebateTurnEvent
    /// (e.g. Mediator interjection in the philosophy module, per-module mediators elsewhere).
    /// Use D1History/D2History/D1LastText/D2LastText to inject context.
    /// </summary>
    protected virtual IAsyncEnumerable<DebateTurnEvent> OnStalemateAsync(
        StalemateSignal signal, string theme, int turn, int level, CancellationToken ct)
        => EmptyEvents();

    /// <summary>
    /// Called per-turn when equilibrium has not been reached and no stalemate
    /// fired this turn. Yield zero or more DebateTurnEvent for ongoing mediation.
    /// </summary>
    protected virtual IAsyncEnumerable<DebateTurnEvent> OnPerTurnMediationAsync(
        string theme, int turn, int level, CancellationToken ct)
        => EmptyEvents();

    private protected static async IAsyncEnumerable<DebateTurnEvent> EmptyEvents()
    {
        yield break;
    }

    /// <summary>
    /// Returns the node type to use when storing a synthesis node for the given
    /// moderator. Override in derived classes to map specific moderator names
    /// to distinct node types (e.g. "mediator" -> "mediator_synthesis").
    /// Base implementation returns _config.SynthesisNodeType for all moderators.
    /// </summary>
    protected virtual string GetSynthesisNodeType(string moderatorName)
        => _config.SynthesisNodeType;

    /// <summary>
    /// Returns all synthesis node type strings that should be considered when
    /// looking up the previous level's synthesis node. Override in derived
    /// classes to add module-specific types.
    /// </summary>
    protected virtual IEnumerable<string> GetSynthesisNodeTypeLookups()
    {
        yield return _config.SynthesisNodeType;
    }

    /// <summary>
    /// Returns the display name for a moderator used as SpeakerId in synthesis
    /// events and node storage. Override in derived classes to map specific
    /// moderator types to names (e.g. MediatorAgent -> "mediator").
    /// Base fallback is "moderator".
    /// </summary>
    protected virtual string GetModeratorName(IModerator moderator) => "moderator";

    // -------------------------------------------------------------------------
    // Non-streaming path (used by batch / test callers)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Non-streaming entry point. Runs a complete dialectical level: debate loop, equilibrium detection, synthesis, and storage. Returns the synthesis result including the next theme for ascent.
    /// </summary>
    public async Task<DialogueLevelResult> AscendLevelAsync(int level, string theme, string sessionId, DialecticalConfiguration config, CancellationToken ct)
    {
        using var usageScope = LlmUsageScope.Begin(sessionId);
        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["SessionId"] = sessionId,
            ["AscentLevel"] = level,
            ["DebateTheme"] = theme,
            ["TelemetryKind"] = "Debate"
        });
        _config = config;
        _ascentContextBlock = string.Empty;
        _moveSuggestionsIssued.Clear();
        theme = InputSanitizer.SanitizeText(theme);
        var embeddingGenerator = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
        var themeEmb = (await embeddingGenerator.GenerateAsync([theme], cancellationToken: ct))[0].Vector.ToArray();
        var questionParent = level > 0 ? await GetLatestSynthesisHashAsync(level - 1, sessionId) : null;
        var questionHash = await GetOrCreateQuestionNodeAsync(level, theme, themeEmb, questionParent, sessionId);

        RootTheme = await ResolveRootThemeAsync(theme, sessionId);
        var rootTheme = RootTheme;

        var d1History = _config.Dialectician1.CreateHistory(theme, rootTheme);
        var d2History = _config.Dialectician2.CreateHistory(theme, rootTheme);
        await InjectAscentContextAsync(d1History, d2History, level, sessionId);
        await InjectClaimMemoryAsync(sessionId, rootTheme, ct);

        _saturationBoostCount = 0;

        var distanceWindow = new Queue<float>();
        var variabilityWindow = new Queue<float>();
        var equilibrium = false;
        var turn = 0;

        float[]? prevD1Emb = null;
        float[]? prevD2Emb = null;

        var lastD1Text = level > 0
            ? $"We now turn to the next rung of our inquiry: {theme}"
            : "Let us begin the inquiry.";
        var lastD2Text = level > 0
            ? $"We now turn to the next rung of our inquiry: {theme}"
            : "Let us begin the inquiry.";
        var currentPhase = "emergence";
        ResponseQualityMetrics? lastD1Metrics = null;
        ResponseQualityMetrics? lastD2Metrics = null;
        double prevD1QualityScore = 50.0;
        double prevD2QualityScore = 50.0;
        double d1PreviousMomentum = 0.0;
        double d2PreviousMomentum = 0.0;
        string? previousResponse = null;

        // Every turn's replies, kept so the full round-by-round debate can be persisted rather
        // than only the final reply per agent. See StoreTurnNodesAsync.
        var batchTurns = new List<TurnRecord>();
        string? d1Reasoning = null, d2Reasoning = null;

        while (!equilibrium && turn < _maxTurns)
        {
            if (turn == 0)
            {
                var pd = BuildAscentDirective(level, turn, currentPhase, lastD1Metrics, theme, null, _config.Dialectician1);
                var d1Prompt = pd is { Length: > 0 }
                    ? $"[User]:\n{theme}\n\n{pd}"
                    : $"[User]:\n{theme}";
                d1History.AddUserMessage(ComposeUserTurn(d1Prompt));
            }
            else
            {
                var d1Evl = EmotionalVolatilityComputer.Compute(
                    ownMetrics: lastD1Metrics,
                    opponentMetrics: lastD2Metrics,
                    previousQualityScore: prevD1QualityScore,
                    phase: currentPhase,
                    turn: turn);
                var pd = BuildAscentDirective(level, turn, currentPhase, lastD1Metrics, theme, d1Evl, _config.Dialectician1, d1PreviousMomentum);
                d1PreviousMomentum = d1Evl.Momentum;
                d1History.AddUserMessage(
                    pd is { Length: > 0 }
                        ? $"[{_config.Dialectician2SpeakerId}]:\n{lastD2Text}\n\n{pd}"
                        : $"[{_config.Dialectician2SpeakerId}]:\n{lastD2Text}");
            }
            var d1AgentResponse = _saturationBoostCount > 0
                ? await _config.Dialectician1.GetResponseAsync(d1History, options.SaturationBoostIntensity, ct)
                : await _config.Dialectician1.GetResponseAsync(d1History, ct);
            d1Reasoning = d1AgentResponse.Reasoning;
            var d1Response = d1AgentResponse.Content;
            if (!string.IsNullOrWhiteSpace(d1Reasoning))
                logger.LogDebug(_config.Dialectician1NodeType + " reasoning (level {Level} turn {Turn}): {Reasoning}", level, turn, d1Reasoning);
            d1History.AddAssistantMessage(d1Response);

            var d1Metrics = qualityGates.AnalyzeResponse(d1Response, previousResponse, turn, ownPreviousResponse: turn > 0 ? lastD1Text : null);
            lastD1Text = d1Response;
            previousResponse = d1Response;
            prevD1QualityScore = lastD1Metrics?.QualityScore ?? 50.0;
            lastD1Metrics = d1Metrics;

            // On turn 0, D2 has not yet spoken - give it the theme directly, same as D1.
            // Feeding D1's text on turn 0 causes small models to echo D1's vocabulary.
            if (turn == 0)
            {
                var pd0 = BuildAscentDirective(level, turn, currentPhase, lastD2Metrics, theme, null, _config.Dialectician2);
                var d2Prompt = pd0 is { Length: > 0 }
                    ? $"[User]:\n{theme}\n\n{pd0}"
                    : $"[User]:\n{theme}";
                d2History.AddUserMessage(ComposeUserTurn(d2Prompt));
            }
            else
            {
                var d2Evl = EmotionalVolatilityComputer.Compute(
                    ownMetrics: lastD2Metrics,
                    opponentMetrics: lastD1Metrics,
                    previousQualityScore: prevD2QualityScore,
                    phase: currentPhase,
                    turn: turn);
                var ad = BuildAscentDirective(level, turn, currentPhase, lastD2Metrics, theme, d2Evl, _config.Dialectician2, d2PreviousMomentum);
                d2PreviousMomentum = d2Evl.Momentum;
                d2History.AddUserMessage(
                    ad is { Length: > 0 }
                        ? $"[{_config.Dialectician1SpeakerId}]:\n{lastD1Text}\n\n{ad}"
                        : $"[{_config.Dialectician1SpeakerId}]:\n{lastD1Text}");
            }
            var d2AgentResponse = _saturationBoostCount > 0
                ? await _config.Dialectician2.GetResponseAsync(d2History, options.SaturationBoostIntensity, ct)
                : await _config.Dialectician2.GetResponseAsync(d2History, ct);
            d2Reasoning = d2AgentResponse.Reasoning;
            var d2Response = d2AgentResponse.Content;
            if (!string.IsNullOrWhiteSpace(d2Reasoning))
                logger.LogDebug(_config.Dialectician2NodeType + " reasoning (level {Level} turn {Turn}): {Reasoning}", level, turn, d2Reasoning);
            d2History.AddAssistantMessage(d2Response);

            var d2Metrics = qualityGates.AnalyzeResponse(d2Response, previousResponse, turn, ownPreviousResponse: turn > 0 ? lastD2Text : null);
            lastD2Text = d2Response;
            previousResponse = d2Response;
            prevD2QualityScore = lastD2Metrics?.QualityScore ?? 50.0;
            lastD2Metrics = d2Metrics;

            // Trim histories to keep context window manageable
            TrimHistory(d1History);
            TrimHistory(d2History);

            // Batch D1+D2 embeddings into one llama.cpp OpenAI-compatible request.
            var batchEmb = await embeddingGenerator.GenerateAsync([d1Response, d2Response], cancellationToken: ct);
            var d1Emb = batchEmb[0].Vector.ToArray();
            var d2Emb = batchEmb[1].Vector.ToArray();

            // Record this turn so the whole exchange can be persisted after the loop. The
            // embeddings are already computed here, so keeping the turn costs nothing extra.
            // Previously only the final reply per agent was stored, which discarded the
            // intervening argument (a 4-turn level generated 8 replies and kept 2).
            batchTurns.Add(new TurnRecord(turn, d1Response, d2Response, d1Emb, d2Emb, currentPhase, d1Reasoning, d2Reasoning, d1Metrics, d2Metrics, d1AgentResponse.InputTokens, d1AgentResponse.OutputTokens, d2AgentResponse.InputTokens, d2AgentResponse.OutputTokens, d1AgentResponse.ModelId, d2AgentResponse.ModelId));

            var step = EvaluateEquilibriumStep(
                d1Emb, d2Emb,
                ref prevD1Emb, ref prevD2Emb,
                distanceWindow, variabilityWindow,
                d1History, d2History,
                theme, avgNovelty: 0f, turn);

            equilibrium = step.Equilibrium;
            d1History = step.D1History;
            d2History = step.D2History;

            logger.LogInformation("Level {Level} turn {Turn} completed.", level, turn);
            logger.LogInformation("Turn {Turn} distance={Dist:F4}, variability={Var:F4}, phase candidate={Phase}",
                turn, step.Distance, step.Variability, step.Phase);
            currentPhase = step.Phase;
            turn++;
        }

        if (!equilibrium)
        {
            logger.LogWarning("[{SessionId}] No equilibrium reached after {MaxTurns} turns (batch). Forcing synthesis fallback.", sessionId, _maxTurns);
        }

        var batchFinalEmb = await embeddingGenerator.GenerateAsync([lastD1Text, lastD2Text], cancellationToken: ct);
        var d1EmbFinal = batchFinalEmb[0].Vector.ToArray();
        var d2EmbFinal = batchFinalEmb[1].Vector.ToArray();

        var avgVariability = variabilityWindow.Count > 0 ? variabilityWindow.Average() : 0.5f;
        var currentAvgEmb = SimdVectorUtility.AverageEmbeddings(d1EmbFinal, d2EmbFinal);
        var resonance = ComputeResonance(currentAvgEmb);
        var phase = phaseDetector.ComputePhase(distanceWindow, avgVariability, turn);

        var d1Hash    = await StoreNodeAsync(_config.Dialectician1NodeType,    level, lastD1Text,    d1EmbFinal,    questionHash, sessionId, phase, reasoning: d1Reasoning, metrics: lastD1Metrics);
        var d2Hash = await StoreNodeAsync(_config.Dialectician2NodeType, level, lastD2Text, d2EmbFinal, questionHash, sessionId, phase, reasoning: d2Reasoning, metrics: lastD2Metrics);

        // Keep the turns that the loop would otherwise discard. Runs after the final nodes so
        // the chain hangs off the level's question and the final replies keep their identity.
        await StoreTurnNodesAsync(batchTurns, level, sessionId, questionHash,
            _config.Dialectician1NodeType, _config.Dialectician2NodeType);

        var ascension = await SynthesizeAndGateAscensionAsync(theme, rootTheme, lastD1Text, lastD2Text, phase, level, sessionId, ct);
        var cleanedMetaphor = ascension.Metaphor;

        var metaphorEmb = (await embeddingGenerator.GenerateAsync([cleanedMetaphor], cancellationToken: ct))[0].Vector.ToArray();

        var synthesisNodeType = GetSynthesisNodeType(ascension.ModeratorName);
        // Try to resolve a citation provider from the Kernel and fetch authority references for the synthesis (best-effort).
        IReadOnlyList<Citation>? authorities = null;
        try
        {
            var citationProvider = kernel.GetRequiredService<ICitationProvider>();
            if (citationProvider != null)
            {
                authorities = await citationProvider.GetTopCitationsAsync(cleanedMetaphor, topN: options.CitationTopN, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Citation provider invocation failed (non-fatal). Proceeding without authority references.");
        }

        var synthesisHash = await StoreSynthesisNodeAsync(level, cleanedMetaphor, metaphorEmb,
                                                       d1Hash, d2Hash,
                                                       avgVariability, resonance, phase, sessionId,
                                                       ascension.ModeratorName, synthesisNodeType,
                                                       ascension.ModeratorSelectionConfidence,
                                                       authorities?.ToArray(),
                                                       ascension.IsSustained,
                                                       ascension.AscentScore,
                                                       ascension.JoinMediation,
                                                       ascension.JoinNovelty,
                                                       ascension.IrreducibleTension,
                                                       ascension.RemainingDissensus,
                                                       ascension.RepeatsPremise,
                                                       ascension.UnresolvedTensions,
                                                       ascension.TensionIntensity);

        await StoreClaimNodesAsync(sessionId, level, synthesisHash, ascension.Claims, ct);

        _recentSynthesisEmbeddings.Add(metaphorEmb);
        if (_recentSynthesisEmbeddings.Count > options.SynthesisMemoryWindow) _recentSynthesisEmbeddings.RemoveAt(0);

        var allNodes = await merkleStore.GetAllNodesAsync<DialecticalNode>();
        var sessionNodes = allNodes.Where(n => n.SessionId == sessionId && n.NodeType != "session_metadata");
        await sessionService.UpdateSessionAsync(sessionId, sessionNodes.Count(), level);

        // Convert batch turns to DebateTurnEvent objects so the non-streaming path
        // can export child spans to Sentinel just like the streaming path does.
        var debateTurnEvents = new List<DebateTurnEvent>();
        foreach (var t in batchTurns)
        {
            debateTurnEvents.Add(new DebateTurnEvent
            {
                Type = _config.Dialectician1NodeType,
                Turn = t.Turn,
                Content = t.D1Text,
                Level = level,
                SpeakerId = _config.Dialectician1SpeakerId,
                DialecticRole = DialecticRole.Thesis,
                CeremonyScore = t.D1Metrics?.CeremonyScore ?? 0,
                SimilarityToPrevious = t.D1Metrics?.SimilarityToPrevious ?? 0,
                NewArgumentCount = t.D1Metrics?.NewArgumentCount ?? 0,
                QualityScore = t.D1Metrics?.QualityScore ?? 0,
                WordCount = t.D1Metrics?.WordCount ?? 0,
                HasFarewell = t.D1Metrics?.HasFarewell ?? false,
                HasAgreement = t.D1Metrics?.HasAgreement ?? false,
                HasDisagreement = t.D1Metrics?.HasDisagreement ?? false,
                Phase = t.Phase,
                Timestamp = DateTime.UtcNow,
                InputTokens = t.D1InputTokens,
                OutputTokens = t.D1OutputTokens,
                ModelId = t.D1ModelId,
            });
            debateTurnEvents.Add(new DebateTurnEvent
            {
                Type = _config.Dialectician2NodeType,
                Turn = t.Turn,
                Content = t.D2Text,
                Level = level,
                SpeakerId = _config.Dialectician2SpeakerId,
                DialecticRole = DialecticRole.Antithesis,
                CeremonyScore = t.D2Metrics?.CeremonyScore ?? 0,
                SimilarityToPrevious = t.D2Metrics?.SimilarityToPrevious ?? 0,
                NewArgumentCount = t.D2Metrics?.NewArgumentCount ?? 0,
                QualityScore = t.D2Metrics?.QualityScore ?? 0,
                WordCount = t.D2Metrics?.WordCount ?? 0,
                HasFarewell = t.D2Metrics?.HasFarewell ?? false,
                HasAgreement = t.D2Metrics?.HasAgreement ?? false,
                HasDisagreement = t.D2Metrics?.HasDisagreement ?? false,
                Phase = t.Phase,
                Timestamp = DateTime.UtcNow,
                InputTokens = t.D2InputTokens,
                OutputTokens = t.D2OutputTokens,
                ModelId = t.D2ModelId,
            });
        }

        // Fractal echo: the gate could not generalize. Return to the root question rather
        // than carrying an empty next theme. Batch callers receive the marker in the trace
        // and the root question as NextTheme.
        var echoEmitted = false;
        if (ascension.IsSustained &&
            FractalEcho.ShouldEcho(theme, RootTheme, true,
                await HasPriorFractalEchoAsync(sessionId, level, RootTheme)))
        {
            var echo = FractalEcho.Create(RootTheme, level);
            if (echo is not null)
            {
                logger.LogInformation(
                    "[{SessionId}] Fractal echo at level {Level} (batch): returning to root question.",
                    sessionId, level);
                debateTurnEvents.Add(echo);
                echoEmitted = true;
            }
        }

        // The root is the next rung only when the echo actually fired; see
        // FractalEcho.ResolveNextTheme. Computed here, after the echo, because the batch trace
        // carries it on the synthesis event below.
        var nextTheme = FractalEcho.ResolveNextTheme(echoEmitted, RootTheme, ascension.NextTheme);

        // Add synthesis event
        debateTurnEvents.Add(new DebateTurnEvent
        {
            Type = "synthesis",
            Turn = -1,
            Metaphor = cleanedMetaphor,
            NextTheme = nextTheme,
            Level = level,
            ConnectiveSentence = ascension.ConnectiveSentence,
            AscensionValidated = ascension.Validated,
            SynthesisThesis = ascension.SynthesisThesis,
            SpeakerId = ascension.ModeratorName,
            DialecticRole = DialecticRole.Synthesis,
            IsSustained = ascension.IsSustained,
            SustainedReason = ascension.SustainedReason,
            AscentScore = ascension.AscentScore,
            IrreducibleTension = ascension.IrreducibleTension,
            RemainingDissensus = ascension.RemainingDissensus,
            RepeatsPremise = ascension.RepeatsPremise,
            Timestamp = DateTime.UtcNow,
            // No model ID available for synthesis (moderator LLM response not captured here)
        });

        return new DialogueLevelResult
        {
            Level = level,
            SynthesisHash = synthesisHash,
            Metaphor = cleanedMetaphor,
            NextTheme = nextTheme,
            ConnectiveSentence = ascension.ConnectiveSentence,
            AscensionValidated = ascension.Validated,
            AscensionAttempts = ascension.Attempts,
            SynthesisThesis = ascension.SynthesisThesis,
            ModeratorSelectionConfidence = ascension.ModeratorSelectionConfidence,
            IsSustained = ascension.IsSustained,
            AscentScore = ascension.AscentScore,
            IrreducibleTension = ascension.IrreducibleTension,
            RemainingDissensus = ascension.RemainingDissensus,
            RepeatsPremise = ascension.RepeatsPremise,
            DebateTurnEvents = debateTurnEvents,
        };
    }

    // -------------------------------------------------------------------------
    // Streaming path
    // -------------------------------------------------------------------------

    /// <summary>
    /// Streaming entry point. Yields DebateTurnEvents in real-time as the debate unfolds, including dialectician turns, stalemate interjections, and the final synthesis.
    /// Wraps the async iterator in <see cref="LlmUsageScope.Wrap{T}"/> so every MoveNext executes under the ambient session scope (async iterators reset ExecutionContext between moves).
    /// </summary>
    public IAsyncEnumerable<DebateTurnEvent> AscendLevelStreamingAsync(int level, string theme, string sessionId, DialecticalConfiguration config, CancellationToken ct = default)
        => LlmUsageScope.Wrap(sessionId, AscendLevelStreamingCoreAsync(level, theme, sessionId, config, ct), ct);

    private async IAsyncEnumerable<DebateTurnEvent> AscendLevelStreamingCoreAsync(int level, string theme, string sessionId, DialecticalConfiguration config, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["SessionId"] = sessionId,
            ["AscentLevel"] = level,
            ["DebateTheme"] = theme,
            ["TelemetryKind"] = "Debate"
        });
        _config = config;
        _ascentContextBlock = string.Empty;
        _moveSuggestionsIssued.Clear();
        theme = InputSanitizer.SanitizeText(theme);
        var embeddingGenerator = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

        var themeEmb = (await embeddingGenerator.GenerateAsync([theme], cancellationToken: ct))[0].Vector.ToArray();
        var questionParent = level > 0 ? await GetLatestSynthesisHashAsync(level - 1, sessionId) : null;
        var questionHash = await GetOrCreateQuestionNodeAsync(level, theme, themeEmb, questionParent, sessionId);

        RootTheme = await ResolveRootThemeAsync(theme, sessionId);
        var rootTheme = RootTheme;

        D1History = _config.Dialectician1.CreateHistory(theme, rootTheme);
        D2History = _config.Dialectician2.CreateHistory(theme, rootTheme);
        await InjectAscentContextAsync(D1History, D2History, level, sessionId);
        await InjectClaimMemoryAsync(sessionId, rootTheme, ct);

        var distanceWindow    = new Queue<float>();
        var variabilityWindow = new Queue<float>();
        var noveltyWindow = new Queue<float>();
        var equilibrium = false;
        var turn = 0;

        // Every turn's replies, kept so the full round-by-round debate can be persisted rather
        // than only the final reply per agent. See StoreTurnNodesAsync.
        var streamTurns = new List<TurnRecord>();
        string? d1Reasoning = null, d2Reasoning = null;

        float[]? prevD1Emb    = null;
        float[]? prevD2Emb = null;

        D1LastText = level > 0
            ? $"We now turn to the next rung of our inquiry: {theme}"
            : "Let us begin the inquiry.";
        D2LastText = level > 0
            ? $"We now turn to the next rung of our inquiry: {theme}"
            : "Let us begin the inquiry.";

        var qualityState = new DialogueQualityState();
        string? previousResponse = null;

        var recentDistancesDouble = new Queue<double>();

        _interjectionCount = 0;
        _saturationBoostCount = 0;
        _pendingMediatorNotes.Clear();

        ResponseQualityMetrics? lastD1Metrics    = null;
        ResponseQualityMetrics? lastD2Metrics = null;
        double prevD1QualityScore = 50.0;
        double prevD2QualityScore = 50.0;
        double d1PreviousMomentum = 0.0;
        double d2PreviousMomentum = 0.0;
        var currentPhase = "emergence";

        MediationInsights.Clear();

        while (!equilibrium && turn < _maxTurns)
        {
            bool stalemateFired = false;

            // Fold any queued mediation/interjection notes into the next opponent-turn
            // user message (see InjectMediatorNote) rather than appending them as
            // standalone user messages, preserving strict user/assistant alternation.
            var mediationNoteText = string.Join("\n\n", _pendingMediatorNotes);
            _pendingMediatorNotes.Clear();

            // ---- Dialectician 1 turn ----
            if (turn == 0)
            {
                var pd = BuildAscentDirective(level, turn, currentPhase, lastD1Metrics, theme, null, _config.Dialectician1);
                var d1Prompt = pd is { Length: > 0 }
                    ? $"[User]:\n{theme}\n\n{pd}"
                    : $"[User]:\n{theme}";
                D1History.AddUserMessage(ComposeUserTurn(d1Prompt));
            }
            else
            {
                var d1Evl = EmotionalVolatilityComputer.Compute(
                    ownMetrics: lastD1Metrics,
                    opponentMetrics: lastD2Metrics,
                    previousQualityScore: prevD1QualityScore,
                    phase: currentPhase,
                    turn: turn);
                var pd = BuildAscentDirective(level, turn, currentPhase, lastD1Metrics, theme, d1Evl, _config.Dialectician1, d1PreviousMomentum);
                d1PreviousMomentum = d1Evl.Momentum;
                var d1UserTurn = pd is { Length: > 0 }
                    ? $"[{_config.Dialectician2SpeakerId}]:\n{D2LastText}\n\n{pd}"
                    : $"[{_config.Dialectician2SpeakerId}]:\n{D2LastText}";
                if (mediationNoteText.Length > 0)
                    d1UserTurn += $"\n\n{mediationNoteText}";
                D1History.AddUserMessage(d1UserTurn);
            }
            var d1Sw = System.Diagnostics.Stopwatch.StartNew();
            var d1Result = _saturationBoostCount > 0
                ? await _config.Dialectician1.GetResponseAsync(D1History, options.SaturationBoostIntensity, ct)
                : await _config.Dialectician1.GetResponseAsync(D1History, ct);
            d1Sw.Stop();
            var d1Response = d1Result.Content;
            d1Reasoning = d1Result.Reasoning;
            logger.LogInformation(
                "[{SessionId}] D1({Agent}) LLM call: level={Level} turn={Turn} latency={LatencyMs}ms responseChars={Chars}",
                sessionId, _config.Dialectician1SpeakerId, level, turn, d1Sw.ElapsedMilliseconds, d1Response?.Length ?? 0);
            if (!string.IsNullOrWhiteSpace(d1Reasoning))
                logger.LogDebug(_config.Dialectician1NodeType + " reasoning (level {Level} turn {Turn}): {Reasoning}", level, turn, d1Reasoning);
            if (d1Response != null)
            {
                D1History.AddAssistantMessage(d1Response);

                var d1Metrics = qualityGates.AnalyzeResponse(d1Response, previousResponse, turn,
                    ownPreviousResponse: turn > 0 ? D1LastText : null);
                D1LastText = d1Response;
                qualityState.RecordTurn(d1Response, d1Metrics, qualityGates.QualityScoreExitThreshold);
                prevD1QualityScore = lastD1Metrics?.QualityScore ?? 50.0;
                lastD1Metrics = d1Metrics;

                var d1ExitReason = await qualityGates.EvaluateAndLogAsync(qualityState, d1Response, previousResponse,
                    turn, rootTheme, _config.ModuleKey, ct);
                if (d1ExitReason == null)
                {
                    previousResponse = d1Response;
                }

                if (d1ExitReason != null)
                {
                    logger.LogWarning("QualityGate triggered on {Agent} turn {Turn}: {Reason}",
                        _config.Dialectician1NodeType, turn, d1ExitReason);
                    yield return new DebateTurnEvent
                    {
                        Type = _config.Dialectician1NodeType,
                        Turn = turn,
                        Content = d1Response,
                        Level = level,
                        SpeakerId = _config.Dialectician1SpeakerId,
                        DialecticRole = DialecticRole.Thesis,
                        CeremonyScore = d1Metrics.CeremonyScore,
                        SimilarityToPrevious = d1Metrics.SimilarityToPrevious,
                        NewArgumentCount = d1Metrics.NewArgumentCount,
                        QualityScore = d1Metrics.QualityScore,
                        WordCount = d1Metrics.WordCount,
                        HasFarewell = d1Metrics.HasFarewell,
                        HasAgreement = d1Metrics.HasAgreement,
                        HasDisagreement = d1Metrics.HasDisagreement,
                        ExitReason = d1ExitReason.ToString(),
                        IsSecurityFlagged = d1ExitReason == DialogueExitReason.PromptInjectionDetected,
                        SecurityThreatReason = d1ExitReason == DialogueExitReason.PromptInjectionDetected
                            ? "PromptInjectionDetected"
                            : null,
                        InputTokens = d1Result.InputTokens,
                        OutputTokens = d1Result.OutputTokens,
                        ModelId = d1Result.ModelId,
                        Timestamp = DateTime.UtcNow
                    };
                    yield break;
                }

                yield return new DebateTurnEvent
                {
                    Type = _config.Dialectician1NodeType,
                    Turn = turn,
                    Content = d1Response,
                    Level = level,
                    SpeakerId = _config.Dialectician1SpeakerId,
                    DialecticRole = DialecticRole.Thesis,
                    CeremonyScore = d1Metrics.CeremonyScore,
                    SimilarityToPrevious = d1Metrics.SimilarityToPrevious,
                    NewArgumentCount = d1Metrics.NewArgumentCount,
                    QualityScore = d1Metrics.QualityScore,
                    WordCount = d1Metrics.WordCount,
                    HasFarewell = d1Metrics.HasFarewell,
                    HasAgreement = d1Metrics.HasAgreement,
                    HasDisagreement = d1Metrics.HasDisagreement,
                    Phase = currentPhase,
                    InputTokens = d1Result.InputTokens,
                    OutputTokens = d1Result.OutputTokens,
                    ModelId = d1Result.ModelId,
                    Timestamp = DateTime.UtcNow
                };

                logger.LogInformation(
                    "[{SessionId}] D1({Agent}) t={Turn} q={Quality:F2} cer={Ceremony:F2} sim={Similarity:F2} nov={Novelty} wc={WordCount} agr={Agree} dis={Disagree}",
                    sessionId, _config.Dialectician1SpeakerId, turn, d1Metrics.QualityScore, d1Metrics.CeremonyScore,
                    d1Metrics.SimilarityToPrevious, d1Metrics.NewArgumentCount, d1Metrics.WordCount,
                    d1Metrics.HasAgreement, d1Metrics.HasDisagreement);

                // ---- Dialectician 2 turn ----
                // On turn 0, D2 has not yet spoken, so it should stake out its own position
                // independently - not read D1's reply as its prompt. Feeding D1's text here
                // causes small models to echo D1's vocabulary and framing (the "mixed roles"
                // bug). D2 turn 0 mirrors D1 turn 0: the user message is the theme only.
                if (turn == 0)
                {
                    var pd0 = BuildAscentDirective(level, turn, currentPhase, lastD2Metrics, theme, null,
                        _config.Dialectician2);
                    var d2Prompt = pd0 is { Length: > 0 }
                        ? $"[User]:\n{theme}\n\n{pd0}"
                        : $"[User]:\n{theme}";
                    D2History.AddUserMessage(ComposeUserTurn(d2Prompt));
                }
                else
                {
                    var d2Evl = EmotionalVolatilityComputer.Compute(
                        ownMetrics: lastD2Metrics,
                        opponentMetrics: lastD1Metrics,
                        previousQualityScore: prevD2QualityScore,
                        phase: currentPhase,
                        turn: turn);
                    var ard = BuildAscentDirective(level, turn, currentPhase, lastD2Metrics, theme, d2Evl,
                        _config.Dialectician2, d2PreviousMomentum);
                    d2PreviousMomentum = d2Evl.Momentum;
                    var d2UserTurn = ard is { Length: > 0 }
                        ? $"[{_config.Dialectician1SpeakerId}]:\n{D1LastText}\n\n{ard}"
                        : $"[{_config.Dialectician1SpeakerId}]:\n{D1LastText}";
                    if (mediationNoteText.Length > 0)
                        d2UserTurn += $"\n\n{mediationNoteText}";
                    D2History.AddUserMessage(d2UserTurn);
                }

                var d2Sw = System.Diagnostics.Stopwatch.StartNew();
                var d2Result = _saturationBoostCount > 0
                    ? await _config.Dialectician2.GetResponseAsync(D2History, options.SaturationBoostIntensity, ct)
                    : await _config.Dialectician2.GetResponseAsync(D2History, ct);
                d2Sw.Stop();
                var d2Response = d2Result.Content;
                d2Reasoning = d2Result.Reasoning;
                logger.LogInformation(
                    "[{SessionId}] D2({Agent}) LLM call: level={Level} turn={Turn} latency={LatencyMs}ms responseChars={Chars}",
                    sessionId, _config.Dialectician2SpeakerId, level, turn, d2Sw.ElapsedMilliseconds,
                    d2Response?.Length ?? 0);
                if (!string.IsNullOrWhiteSpace(d2Reasoning))
                    logger.LogDebug(
                        _config.Dialectician2NodeType + " reasoning (level {Level} turn {Turn}): {Reasoning}", level,
                        turn, d2Reasoning);
                if (d2Response != null)
                {
                    D2History.AddAssistantMessage(d2Response);

                    var d2Metrics = qualityGates.AnalyzeResponse(d2Response, previousResponse, turn,
                        ownPreviousResponse: turn > 0 ? D2LastText : null);
                    D2LastText = d2Response;
                    qualityState.RecordTurn(d2Response, d2Metrics, qualityGates.QualityScoreExitThreshold);
                    prevD2QualityScore = lastD2Metrics?.QualityScore ?? 50.0;
                    lastD2Metrics = d2Metrics;

                    var d2ExitReason = await qualityGates.EvaluateAndLogAsync(qualityState, d2Response,
                        previousResponse,
                        turn, rootTheme, _config.ModuleKey, ct);
                    if (d2ExitReason == null)
                    {
                        previousResponse = d2Response;
                    }

                    if (d2ExitReason != null)
                    {
                        logger.LogWarning("QualityGate triggered on {Agent} turn {Turn}: {Reason}",
                            _config.Dialectician2NodeType, turn, d2ExitReason);
                        yield return new DebateTurnEvent
                        {
                            Type = _config.Dialectician2NodeType,
                            Turn = turn,
                            Content = d2Response,
                            Level = level,
                            SpeakerId = _config.Dialectician2SpeakerId,
                            DialecticRole = DialecticRole.Antithesis,
                            CeremonyScore = d2Metrics.CeremonyScore,
                            SimilarityToPrevious = d2Metrics.SimilarityToPrevious,
                            NewArgumentCount = d2Metrics.NewArgumentCount,
                            QualityScore = d2Metrics.QualityScore,
                            WordCount = d2Metrics.WordCount,
                            HasFarewell = d2Metrics.HasFarewell,
                            HasAgreement = d2Metrics.HasAgreement,
                            HasDisagreement = d2Metrics.HasDisagreement,
                            Phase = currentPhase,
                            ExitReason = d2ExitReason.ToString(),
                            IsSecurityFlagged = d2ExitReason == DialogueExitReason.PromptInjectionDetected,
                            SecurityThreatReason = d2ExitReason == DialogueExitReason.PromptInjectionDetected
                                ? "PromptInjectionDetected"
                                : null,
                            InputTokens = d2Result.InputTokens,
                            OutputTokens = d2Result.OutputTokens,
                            ModelId = d2Result.ModelId,
                            Timestamp = DateTime.UtcNow
                        };
                        yield break;
                    }

                    yield return new DebateTurnEvent
                    {
                        Type = _config.Dialectician2NodeType,
                        Turn = turn,
                        Content = d2Response,
                        Level = level,
                        SpeakerId = _config.Dialectician2SpeakerId,
                        DialecticRole = DialecticRole.Antithesis,
                        CeremonyScore = d2Metrics.CeremonyScore,
                        SimilarityToPrevious = d2Metrics.SimilarityToPrevious,
                        NewArgumentCount = d2Metrics.NewArgumentCount,
                        QualityScore = d2Metrics.QualityScore,
                        WordCount = d2Metrics.WordCount,
                        HasFarewell = d2Metrics.HasFarewell,
                        HasAgreement = d2Metrics.HasAgreement,
                        HasDisagreement = d2Metrics.HasDisagreement,
                        Phase = currentPhase,
                        InputTokens = d2Result.InputTokens,
                        OutputTokens = d2Result.OutputTokens,
                        ModelId = d2Result.ModelId,
                        Timestamp = DateTime.UtcNow
                    };

                    logger.LogInformation(
                        "[{SessionId}] D2({Agent}) t={Turn} q={Quality:F2} cer={Ceremony:F2} sim={Similarity:F2} nov={Novelty} wc={WordCount} agr={Agree} dis={Disagree}",
                        sessionId, _config.Dialectician2SpeakerId, turn, d2Metrics.QualityScore,
                        d2Metrics.CeremonyScore,
                        d2Metrics.SimilarityToPrevious, d2Metrics.NewArgumentCount, d2Metrics.WordCount,
                        d2Metrics.HasAgreement, d2Metrics.HasDisagreement);

                    // Batch D1+D2 embeddings into a single HTTP call.
                    var embSw = System.Diagnostics.Stopwatch.StartNew();
                    var batchEmb =
                        await embeddingGenerator.GenerateAsync([d1Response, d2Response],
                            cancellationToken: ct);
                    embSw.Stop();
                    logger.LogDebug("[{SessionId}] Embedding batch (turn {Turn}): latency={LatencyMs}ms", sessionId,
                        turn,
                        embSw.ElapsedMilliseconds);
                    var d1Emb = batchEmb[0].Vector.ToArray();
                    var d2Emb = batchEmb[1].Vector.ToArray();

                    // Record the turn so the full exchange can be persisted after the loop. The
                    // embeddings are already computed here, so keeping the turn costs nothing extra.
                    // Previously only the final reply per agent survived, discarding the argument.
                    streamTurns.Add(new TurnRecord(turn, d1Response, d2Response, d1Emb, d2Emb, currentPhase,
                        d1Reasoning,
                        d2Reasoning, d1Metrics, d2Metrics, d1Result.InputTokens, d1Result.OutputTokens,
                        d2Result.InputTokens, d2Result.OutputTokens));

                    var turnNovelty = (d1Metrics.NewArgumentCount + d2Metrics.NewArgumentCount) / 2f;
                    noveltyWindow.Enqueue(turnNovelty);
                    if (noveltyWindow.Count > _stabilityWindow) noveltyWindow.Dequeue();
                    var avgNovelty = noveltyWindow.Count > 0 ? noveltyWindow.Average() : 0f;

                    // Shared equilibrium-detection step (distance/variability windows,
                    // behavior preserved: prev-emb tracking, phase, saturation/crisis boost).
                    var step = EvaluateEquilibriumStep(
                        d1Emb, d2Emb,
                        ref prevD1Emb, ref prevD2Emb,
                        distanceWindow, variabilityWindow,
                        D1History, D2History,
                        theme, avgNovelty, turn);

                    D1History = step.D1History;
                    D2History = step.D2History;
                    equilibrium = step.Equilibrium;

                    // Distance series used by stalemate detection.
                    recentDistancesDouble.Enqueue(step.Distance);
                    if (recentDistancesDouble.Count > qualityGates.PremiseForkSustainedTurns + 1)
                        recentDistancesDouble.Dequeue();

                    // ---- Stalemate detection (shared) ----
                    if (qualityGates.Enabled && _interjectionCount < _maxInterjections)
                    {
                        var historyCount = qualityState.ResponseHistory.Count;
                        var twoTurnsAgoD1 = historyCount >= 4 ? qualityState.ResponseHistory[historyCount - 4] : null;
                        var twoTurnsAgoD2 = historyCount >= 3 ? qualityState.ResponseHistory[historyCount - 3] : null;

                        var d1CircularSim = twoTurnsAgoD1 != null
                            ? qualityGates.CalculateTextSimilarity(d1Response, twoTurnsAgoD1)
                            : 0.0;
                        var d2CircularSim = twoTurnsAgoD2 != null
                            ? qualityGates.CalculateTextSimilarity(d2Response, twoTurnsAgoD2)
                            : 0.0;

                        var stalemate = qualityGates.DetectStalemate(
                            qualityState,
                            d1Response,
                            d2Response,
                            _config.Dialectician1SpeakerId,
                            _config.Dialectician2SpeakerId,
                            d1CircularSim,
                            d2CircularSim,
                            step.Distance,
                            recentDistancesDouble,
                            turn);

                        if (stalemate.HasValue)
                        {
                            stalemateFired = true;
                            _interjectionCount++;
                            // Stalemate indicates the debate is entrenched/stuck rather than genuinely converged.
                            // Reset equilibrium to false so the agents continue debating and respond to the interjection.
                            equilibrium = false;

                            logger.LogInformation(
                                "Stalemate detected: {Signal} (interjection {Count}/{Max}) at turn {Turn}. Resetting equilibrium to continue dialogue.",
                                stalemate.Value, _interjectionCount, _maxInterjections, turn);

                            await foreach (var evt in OnStalemateAsync(stalemate.Value, theme, turn, level, ct))
                            {
                                yield return evt;
                            }
                        }
                    }
                    // ---- End stalemate block ----

                    // Per-turn mediation hook (when equilibrium not reached and no stalemate this turn)
                    if (!stalemateFired && !equilibrium)
                    {
                        await foreach (var evt in OnPerTurnMediationAsync(theme, turn, level, ct))
                        {
                            yield return evt;
                        }
                    }

                    // Trim histories to keep context window manageable
                    TrimHistory(D1History);
                    TrimHistory(D2History);

                    var meanDistLog = distanceWindow.Count > 0 ? distanceWindow.Average() : 0f;
                    var stdDevDistLog = distanceWindow.Count > 1
                        ? (float)Math.Sqrt(distanceWindow.Average(d => (d - meanDistLog) * (d - meanDistLog)))
                        : float.NaN;

                    logger.LogInformation(
                        "[{SessionId}] Turn {Turn} completed: dist={Dist:F4} stdDev={StdDev:F4} var={Var:F4} avgNov={AvgNov:F2} phase={Phase} eq={Eq}",
                        sessionId, turn, step.Distance, stdDevDistLog, step.Variability, avgNovelty, step.Phase,
                        equilibrium);
                    currentPhase = step.Phase;
                }
            }

            turn++;
        }

        if (!equilibrium)
        {
            logger.LogWarning("[{SessionId}] No equilibrium reached after {MaxTurns} turns. Forcing synthesis fallback.", sessionId, _maxTurns);
        }

        var avgDistFinal   = distanceWindow.Count > 0 ? distanceWindow.Average() : 0f;
        var avgVarFinal    = variabilityWindow.Count > 0 ? variabilityWindow.Average() : 0f;
        var avgNovFinal    = noveltyWindow.Count > 0 ? noveltyWindow.Average() : 0f;
        logger.LogInformation(
            "[{SessionId}] Level {Level} COMPLETE: turns={Turns} equilibrium={Eq} phase={Phase} avgDist={AvgDist:F4} avgVar={AvgVar:F4} avgNovelty={AvgNov:F2} interjections={Interjections} saturationBoosts={Boosts}",
            sessionId, level, turn, equilibrium, currentPhase, avgDistFinal, avgVarFinal, avgNovFinal, _interjectionCount, _saturationBoostCount);

        var batchFinalEmb = await embeddingGenerator.GenerateAsync([D1LastText, D2LastText], cancellationToken: ct);
        var d1EmbFinal = batchFinalEmb[0].Vector.ToArray();
        var d2EmbFinal = batchFinalEmb[1].Vector.ToArray();

        var avgVariability = variabilityWindow.Count > 0 ? variabilityWindow.Average() : 0.5f;
        var currentAvgEmb = SimdVectorUtility.AverageEmbeddings(d1EmbFinal, d2EmbFinal);
        var resonance = ComputeResonance(currentAvgEmb);
        var phase = phaseDetector.ComputePhase(distanceWindow, avgVariability, turn,
            noveltyWindow.Count > 0 ? noveltyWindow.Average() : 0f);

        var d1Hash    = await StoreNodeAsync(_config.Dialectician1NodeType,    level, D1LastText,    d1EmbFinal,    questionHash, sessionId, phase, reasoning: d1Reasoning, metrics: lastD1Metrics);
        var d2Hash = await StoreNodeAsync(_config.Dialectician2NodeType, level, D2LastText, d2EmbFinal, questionHash, sessionId, phase, reasoning: d2Reasoning, metrics: lastD2Metrics);

        // Keep the turns that the loop would otherwise discard.
        await StoreTurnNodesAsync(streamTurns, level, sessionId, questionHash,
            _config.Dialectician1NodeType, _config.Dialectician2NodeType);

        var ascension = await SynthesizeAndGateAscensionAsync(theme, rootTheme, D1LastText, D2LastText, phase, level, sessionId, ct, MediationInsights.ToString());
        var cleanedMetaphor = ascension.Metaphor;

        // Fractal echo. The gate exhausted its candidates without validating a
        // generalization, so the vocabulary of this rung is spent. Yield the return marker
        // BEFORE the synthesis event: the frontend closes the SSE stream as soon as it sees
        // synthesis, so an event emitted afterwards would never reach it.
        var echoEmitted = false;
        if (ascension.IsSustained &&
            FractalEcho.ShouldEcho(theme, RootTheme, true,
                await HasPriorFractalEchoAsync(sessionId, level, RootTheme)))
        {
            var echo = FractalEcho.Create(RootTheme, level);
            if (echo is not null)
            {
                logger.LogInformation(
                    "[{SessionId}] Fractal echo at level {Level}: returning to root question.",
                    sessionId, level);
                yield return echo;
                echoEmitted = true;
            }
        }

        // The root is the next rung only when the echo actually fired. A sustained rung with no
        // echo means the root has already been reopened, or the rung is the root itself; chaining
        // anyway would re-ask it until the level ceiling. See FractalEcho.ResolveNextTheme.
        var nextTheme = FractalEcho.ResolveNextTheme(echoEmitted, RootTheme, ascension.NextTheme);

        foreach (var rejectionReason in ascension.RejectionReasons)
        {
            yield return new DebateTurnEvent
            {
                Type = "gate_rejection",
                Turn = -1,
                Content = rejectionReason,
                Level = level,
                AscensionValidated = false,
                DialecticRole = DialecticRole.Synthesis,
                Timestamp = DateTime.UtcNow,
            };
        }

        var metaphorEmb = (await embeddingGenerator.GenerateAsync([cleanedMetaphor], cancellationToken: ct))[0].Vector.ToArray();

        var synthesisNodeType = GetSynthesisNodeType(ascension.ModeratorName);
        // Try to resolve a citation provider from the Kernel and fetch authority references for the synthesis (best-effort).
        IReadOnlyList<Citation>? authorities = null;
        try
        {
            var citationProvider = kernel.GetRequiredService<ICitationProvider>();
            authorities = await citationProvider.GetTopCitationsAsync(cleanedMetaphor, topN: options.CitationTopN, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Citation provider invocation failed (non-fatal). Proceeding without authority references.");
        }

        var synthesisHash = await StoreSynthesisNodeAsync(level, cleanedMetaphor, metaphorEmb,
                                                       d1Hash, d2Hash,
                                                       avgVariability, resonance, phase, sessionId,
                                                       ascension.ModeratorName, synthesisNodeType,
                                                       ascension.ModeratorSelectionConfidence,
                                                       authorities?.ToArray(),
                                                       ascension.IsSustained,
                                                       ascension.AscentScore,
                                                       ascension.JoinMediation,
                                                       ascension.JoinNovelty,
                                                       ascension.IrreducibleTension,
                                                       ascension.RemainingDissensus,
                                                       ascension.RepeatsPremise,
                                                       ascension.UnresolvedTensions,
                                                       ascension.TensionIntensity);

        await StoreClaimNodesAsync(sessionId, level, synthesisHash, ascension.Claims, ct);

        _recentSynthesisEmbeddings.Add(metaphorEmb);
        if (_recentSynthesisEmbeddings.Count > options.SynthesisMemoryWindow) _recentSynthesisEmbeddings.RemoveAt(0);

        var allNodes = await merkleStore.GetAllNodesAsync<DialecticalNode>();
        var sessionNodes = allNodes.Where(n => n.SessionId == sessionId && n.NodeType != "session_metadata");
        await sessionService.UpdateSessionAsync(sessionId, sessionNodes.Count(), level);

        yield return new DebateTurnEvent
        {
            Type = "synthesis",
            Turn = -1,
            Metaphor = cleanedMetaphor,
            NextTheme = nextTheme,
            Level = level,
            ConnectiveSentence = ascension.ConnectiveSentence,
            AscensionValidated = ascension.Validated,
            SynthesisThesis = ascension.SynthesisThesis,
            SpeakerId = ascension.ModeratorName,
            DialecticRole = DialecticRole.Synthesis,
            IsSustained = ascension.IsSustained,
            SustainedReason = ascension.SustainedReason,
            AscentScore = ascension.AscentScore,
            IrreducibleTension = ascension.IrreducibleTension,
            RemainingDissensus = ascension.RemainingDissensus,
            RepeatsPremise = ascension.RepeatsPremise,
            Timestamp = DateTime.UtcNow,
        };
    }

    // -------------------------------------------------------------------------
    // Shared private helpers
    // -------------------------------------------------------------------------

    private bool IsStable(Queue<float> distances, Queue<float> variabilities, float stabilityThreshold)
    {
        if (distances.Count < _stabilityWindow) return false;
        var meanDist = distances.Average();
        var stdDevDist = (float)Math.Sqrt(distances.Average(d => (d - meanDist) * (d - meanDist)));
        var avgVar = variabilities.Count > 0 ? variabilities.Average() : 0f;
        return stdDevDist < stabilityThreshold && avgVar > _variabilityMinimum;
    }

    /// <summary>
    /// Trims a dialectician's chat history to retain only the most recent turn pairs,
    /// preserving the system prompt (index 0) and the first user turn (index 1), which
    /// embeds the ascent context when one is present (content starts with "[ASCENT CONTEXT").
    /// Each turn consists of 2 messages: a user message (opponent's text) and an assistant
    /// message (own response). The trim always removes whole turn pairs (an even number of
    /// messages) so that the strict user/assistant alternation required by the llama.cpp
    /// Mistral template is never broken - removing a lone message would otherwise leave two
    /// consecutive user messages and cause a 400 "Conversation roles must alternate" error.
    /// This reduces LLM context size and improves inference speed without losing the initial
    /// question or recent debate context.
    /// </summary>
    private void TrimHistory(ChatHistory history)
    {
        if (_maxContextTurns <= 0) return;

        // Determine the baseline: always keep index 0 (system prompt).
        // If the first user turn embeds the ascent context, keep it too.
        var baselineIndex = 1; // At minimum, keep index 0
        if (history.Count > 1 && history[1].Role == AuthorRole.User)
        {
            // Check if index 1 embeds the ascent context (starts with "[ASCENT CONTEXT")
            var content = history[1].Content ?? string.Empty;
            if (content.StartsWith("[ASCENT CONTEXT", StringComparison.OrdinalIgnoreCase))
            {
                baselineIndex = 2; // Keep both index 0 and 1
            }
        }

        // Messages beyond baseline are turn pairs (user + assistant).
        var messagesBeyondBaseline = history.Count - baselineIndex;
        var maxMessagesToKeep = _maxContextTurns * 2; // 2 messages per turn pair

        if (messagesBeyondBaseline <= maxMessagesToKeep)
        {
            return; // No trimming needed
        }

        // Calculate how many messages to trim from the beginning (after baseline)
        var messagesToTrim = messagesBeyondBaseline - maxMessagesToKeep;

        // Round down to whole turn pairs so alternation is preserved. Trimming a lone
        // message (e.g. an assistant whose paired user was kept as the ascent-context
        // baseline) would produce consecutive same-role messages and a 400 from llama.cpp.
        if (messagesToTrim % 2 != 0)
        {
            messagesToTrim--;
        }

        // Remove the oldest messages beyond baseline, a whole turn pair at a time.
        for (var i = 0; i < messagesToTrim; i++)
        {
            history.RemoveAt(baselineIndex);
        }

        logger.LogDebug(
            "Trimmed {Trimmed} messages from history (kept {Kept} messages: baseline={Baseline}, recent turns={Turns}).",
            messagesToTrim, history.Count, baselineIndex, _maxContextTurns);
    }

    private float ComputeResonance(float[] currentEmb)
    {
        if (_recentSynthesisEmbeddings.Count == 0) return 0.5f;
        return _recentSynthesisEmbeddings.Max(emb => SimdVectorUtility.CosineSimilarity(currentEmb, emb));
    }

    private async Task<string> ResolveRootThemeAsync(string currentTheme, string sessionId)
    {
        var session = await sessionService.GetSessionAsync(sessionId);
        return string.IsNullOrWhiteSpace(session?.RootQuestion) ? currentTheme : session.RootQuestion;
    }

    /// <summary>
    /// True when a prior level in this session already returned to the same root question:
    /// a sustained synthesis followed by a question whose content equals the root. Used as
    /// the cross-level half of the one-echo-per-root guard (the scoped service instance
    /// does not survive between level requests).
    /// </summary>
    private async Task<bool> HasPriorFractalEchoAsync(string sessionId, int level, string rootTheme)
    {
        if (level <= 0 || string.IsNullOrWhiteSpace(rootTheme)) return false;

        var nodes = await merkleStore.GetAllNodesAsync<DialecticalNode>();
        var sessionNodes = nodes.Where(n => n.SessionId == sessionId).ToList();
        if (sessionNodes.Count == 0) return false;

        var synthesisTypes = GetSynthesisNodeTypeLookups().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var root = rootTheme.Trim();

        foreach (var synthesis in sessionNodes.Where(n =>
                     n.Level < level &&
                     n.IsSustained &&
                     synthesisTypes.Contains(n.NodeType)))
        {
            var nextQuestion = sessionNodes.FirstOrDefault(n =>
                string.Equals(n.NodeType, "question", StringComparison.OrdinalIgnoreCase) &&
                n.Level == synthesis.Level + 1);

            if (nextQuestion is not null &&
                string.Equals(nextQuestion.Content?.Trim(), root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Seeds both dialectician histories with a compact summary of every completed
    /// prior round (its theme and the ending synthesis) so the new round continues
    /// the ascent from established conclusions instead of restarting from the root
    /// question. This is what makes the narrative learn and ascend round by round.
    /// No-op for level 0 or when no prior synthesis nodes exist.
    /// </summary>
    private async Task InjectAscentContextAsync(ChatHistory d1History, ChatHistory d2History, int level, string sessionId)
    {
        var context = await BuildAscentContextAsync(level, sessionId);
        if (string.IsNullOrWhiteSpace(context)) return;

        _ascentContextBlock =
            "[ASCENT CONTEXT - already established in earlier rounds of this same inquiry. " +
            "Build on these conclusions and advance the narrative to a higher rung; do not " +
            "restart from the root question. This context is for your eyes only and must " +
            "never be echoed in your reply.]\n" + context;
    }

    /// <summary>
    /// The rungs already climbed in this session's inquiry, oldest first, excluding the level
    /// currently being synthesised. This is the moderator's ladder memory: the next-theme prompt
    /// used to receive only the root, the current question and the metaphor, so consecutive rungs
    /// restated earlier ones in new words.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetClimbedThemesAsync(int level, string sessionId)
    {
        if (level <= 0 || string.IsNullOrWhiteSpace(sessionId)) return [];

        var nodes = await merkleStore.GetAllNodesAsync<DialecticalNode>();
        return nodes
            .Where(n => n.SessionId == sessionId && n.NodeType == "question" && !string.IsNullOrWhiteSpace(n.Content))
            .GroupBy(n => n.Level)
            .Where(g => g.Key < level)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderByDescending(n => n.Timestamp).First().Content!)
            .ToList();
    }

    /// <summary>
    /// Recalls prior sessions' claims overlapping this session's root question (when
    /// cross-session memory is enabled) and records the resulting grounding block so
    /// <see cref="ComposeUserTurn"/> can fold it into each level's opening user turn.
    /// Memoized per session id so a multi-level ascent only pays the retrieval cost once.
    /// </summary>
    private async Task InjectClaimMemoryAsync(string sessionId, string rootTheme, CancellationToken ct)
    {
        if (!options.EnableCrossSessionMemory || claimMemoryRetriever is null) return;
        if (string.IsNullOrWhiteSpace(rootTheme) || string.IsNullOrWhiteSpace(sessionId)) return;
        if (!_claimMemoryInjectedSessions.Add(sessionId)) return;

        try
        {
            var recalled = await claimMemoryRetriever.RetrieveAsync(
                rootTheme,
                sessionId,
                new ClaimMemoryQueryOptions
                {
                    MaxResults = Math.Max(1, options.CrossSessionMemoryMaxClaims),
                    MinSimilarity = options.CrossSessionMemoryMinSimilarity,
                    ExcludeSameSession = true,
                    Mode = options.CrossSessionMemoryMode,
                },
                ct);

            var block = ClaimMemoryPromptBuilder.Build(recalled, options.CrossSessionMemoryMaxClaims);
            if (string.IsNullOrWhiteSpace(block))
                return;

            _claimMemoryBlock = block;
            logger.LogInformation(
                "Cross-session memory: grounded level with {Count} prior claim(s) for session {SessionId}.",
                recalled.Count, sessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Memory is an augmentation, never a hard dependency: a retrieval failure must
            // not fail or degrade the debate, so fall back to an un-grounded level.
            logger.LogWarning(ex, "Cross-session memory recall failed for session {SessionId} (non-fatal).", sessionId);
        }
    }

    /// <summary>
    /// Prepends the ascending-context block (when present) to a turn's user message.
    /// The block is folded into the first user message rather than emitted as a separate
    /// user turn so that consecutive user messages never occur in the dialectician histories,
    /// which strict chat templates (e.g. llama.cpp Mistral) reject with a 400.
    /// </summary>
    private string ComposeUserTurn(string baseMessage)
    {
        var memory = _claimMemoryBlock.Length > 0 ? _claimMemoryBlock + "\n\n" : string.Empty;
        var context = _ascentContextBlock.Length > 0 ? _ascentContextBlock + "\n\n" : string.Empty;
        return context + memory + baseMessage;
    }

    /// <summary>
    /// Collects, for the most recent N levels below the current one (controlled by
    /// <see cref="DialecticsOptions.MaxAscentContextLevels"/>), the round's theme
    /// (question node) and its ending synthesis metaphor. This implements fixed-context
    /// architecture: only the most recent N rounds' syntheses are carried forward,
    /// preventing unbounded context growth across levels. When MaxAscentContextLevels
    /// is 0 or negative, all previous levels are included (legacy behavior).
    /// Returns an empty string when there is nothing to carry forward.
    /// </summary>
    private async Task<string> BuildAscentContextAsync(int level, string sessionId)
    {
        if (level <= 0 || string.IsNullOrWhiteSpace(sessionId)) return string.Empty;

        var nodes = await merkleStore.GetAllNodesAsync<DialecticalNode>();
        var synthesisTypes = GetSynthesisNodeTypeLookups().ToHashSet();

        var questionByLevel = nodes
            .Where(n => n.SessionId == sessionId && n.NodeType == "question" && !string.IsNullOrWhiteSpace(n.Content))
            .GroupBy(n => n.Level)
            .Select(g => new { Level = g.Key, Theme = g.OrderByDescending(n => n.Timestamp).First().Content })
            .ToDictionary(x => x.Level, x => x.Theme);

        var synthesisByLevel = nodes
            .Where(n => n.SessionId == sessionId && synthesisTypes.Contains(n.NodeType) && !string.IsNullOrWhiteSpace(n.Content))
            .GroupBy(n => n.Level)
            .Select(g => new { Level = g.Key, Metaphor = g.OrderByDescending(n => n.Timestamp).First().Content })
            .ToDictionary(x => x.Level, x => x.Metaphor);

        var sb = new StringBuilder();
        var startLevel = _maxAscentContextLevels > 0 ? Math.Max(0, level - _maxAscentContextLevels) : 0;
        for (var lvl = startLevel; lvl < level; lvl++)
        {
            if (!synthesisByLevel.TryGetValue(lvl, out var metaphor)) continue;
            var theme = questionByLevel.GetValueOrDefault(lvl, "(unstated theme)");
            sb.AppendLine($"Round {lvl + 1} - the inquiry into \"{theme}\" ended in the synthesis: {metaphor}");
        }

        return sb.ToString().Trim();
    }

    private async Task<string?> GetLatestSynthesisHashAsync(int level, string sessionId)
    {
        var nodes = await merkleStore.GetAllNodesAsync<DialecticalNode>();
        var synthesisTypes = GetSynthesisNodeTypeLookups().ToHashSet();
        var synthesis = nodes
            .Where(n => synthesisTypes.Contains(n.NodeType) && n.Level == level && n.SessionId == sessionId)
            .OrderByDescending(n => n.Timestamp)
            .FirstOrDefault();

        // Fallback: if the previous level was interrupted before its synthesis
        // was stored, link to its latest node of ANY type. This keeps the
        // Merkle chain connected so the frontend's latest-path filter never
        // orphans an entire round.
        if (synthesis is null)
        {
            synthesis = nodes
                .Where(n => n.Level == level && n.SessionId == sessionId)
                .OrderByDescending(n => n.Timestamp)
                .FirstOrDefault();
        }

        return synthesis?.GetCid().ToString();
    }

    private async Task<string> GetOrCreateQuestionNodeAsync(int level, string theme, float[] themeEmb, string? questionParent, string sessionId)
    {
        var nodes = await merkleStore.GetAllNodesAsync<DialecticalNode>();
        var existingQuestion = nodes.FirstOrDefault(n =>
            n.NodeType == "question" && n.Level == level && n.SessionId == sessionId);

        if (existingQuestion != null)
        {
            return existingQuestion.GetCid().ToString();
        }

        return await StoreNodeAsync("question", level, theme, themeEmb, questionParent, sessionId);
    }

    /// <summary>
    /// One turn of a level's debate: both agents' replies, the embeddings already computed
    /// for that turn, and the quality metrics the gates scored them with.
    /// Token fields capture per-turn LLM usage so Sentinel child spans can carry cost.
    /// </summary>
    private sealed record TurnRecord(
        int Turn, string D1Text, string D2Text, float[] D1Emb, float[] D2Emb, string Phase,
        string? D1Reasoning = null, string? D2Reasoning = null,
        ResponseQualityMetrics? D1Metrics = null, ResponseQualityMetrics? D2Metrics = null,
        long? D1InputTokens = null, long? D1OutputTokens = null,
        long? D2InputTokens = null, long? D2OutputTokens = null,
        string? D1ModelId = null, string? D2ModelId = null);

    /// <summary>
    /// Persists the full round-by-round exchange for a level.
    ///
    /// Previously the debate loop overwrote <c>lastD1Text</c>/<c>lastD2Text</c> each turn and
    /// stored only the final reply per agent, so a 4-turn level generated 8 replies and kept 2.
    /// The dropped turns are the actual argument - how each side answered the other's moves -
    /// so discarding them discarded the reasoning, not just text. They were generated and paid
    /// for, then thrown away.
    ///
    /// Writes the intermediate turns only (all but the last), chained in order via ParentHash so
    /// the exchange sequence is recoverable from the DAG. The final replies keep their existing
    /// node types and Thesis/Antithesis roles, so anything selecting "the level's thesis" is
    /// unaffected.
    /// </summary>
    private async Task StoreTurnNodesAsync(
        IReadOnlyList<TurnRecord> turns, int level, string sessionId, string questionHash,
        string d1NodeType, string d2NodeType)
    {
        if (turns.Count <= 1)
            return;   // nothing intermediate to keep

        var parent = questionHash;
        for (var i = 0; i < turns.Count - 1; i++)
        {
            var t = turns[i];
            parent = await StoreTurnNodeAsync(d1NodeType, level, t.Turn, t.D1Text, t.D1Emb, parent, sessionId, t.Phase, t.D1Reasoning, t.D1Metrics);
            parent = await StoreTurnNodeAsync(d2NodeType, level, t.Turn, t.D2Text, t.D2Emb, parent, sessionId, t.Phase, t.D2Reasoning, t.D2Metrics);
        }

        logger.LogInformation(
            "[{SessionId}] Persisted {Count} intermediate turns at level {Level} ({Replies} replies) " +
            "that were previously discarded.",
            sessionId, turns.Count - 1, level, (turns.Count - 1) * 2);
    }

    private async Task<string> StoreNodeAsync(string nodeType, int level, string content, float[] embedding, string? parentHash = null, string sessionId = "", string phase = "emergence", int? turn = null, string? reasoning = null, ResponseQualityMetrics? metrics = null)
    {
        var node = new DialecticalNode
        {
            NodeType = nodeType,
            Level = level,
            Content = content.StripReasoningTags(),
            Embedding = embedding,
            ParentHash = parentHash,
            Timestamp = DateTime.UtcNow,
            SessionId = sessionId,
            CyclePhase = phase,
            Turn = turn,
            ReasoningTrace = _llmOptions.PersistReasoningTrace ? reasoning : null,
            QualityScore = metrics?.QualityScore,
            CeremonyScore = metrics?.CeremonyScore,
            SimilarityToPrevious = metrics?.SimilarityToPrevious,
            NewArgumentCount = metrics?.NewArgumentCount,
            WordCount = metrics?.WordCount,
            HasFarewell = metrics?.HasFarewell,
            HasAgreement = metrics?.HasAgreement,
            HasDisagreement = metrics?.HasDisagreement,
            SelfSimilarity = metrics?.SelfSimilarity,
        };
        var cid = await merkleStore.PutBlobAsync(node);
        return cid.ToString();
    }

    /// <summary>
    /// Persists every turn of a level as its own node, chained in order, so the full
    /// round-by-round debate survives.
    ///
    /// Why this exists: the debate loop overwrites <c>lastD1Text</c>/<c>lastD2Text</c> each turn
    /// and only the FINAL reply per agent was stored. A 4-turn level generated 8 replies and
    /// kept 2 - roughly 40% of the debate was discarded after being generated and paid for.
    /// The intermediate turns are the actual argumentative work (how each side responded to the
    /// other's moves), so losing them loses the reasoning, not just the text.
    ///
    /// Chained rather than flat: each node's ParentHash is the previous turn, so the order of
    /// the exchange is recoverable from the DAG instead of being inferred from timestamps.
    ///
    /// Only intermediate turns are written here. The final reply per agent keeps its existing
    /// node type and role (Thesis/Antithesis), so anything selecting "the level's thesis" still
    /// finds it unchanged.
    /// </summary>
    private async Task<string> StoreTurnNodeAsync(
        string nodeType, int level, int turn, string content, float[] embedding,
        string? parentHash, string sessionId, string phase, string? reasoning = null,
        ResponseQualityMetrics? metrics = null)
    {
        var node = new DialecticalNode
        {
            NodeType = nodeType,
            Level = level,
            Content = content.StripReasoningTags(),
            Embedding = embedding,
            ParentHash = parentHash,
            Timestamp = DateTime.UtcNow,
            SessionId = sessionId,
            CyclePhase = phase,
            Turn = turn,
            ReasoningTrace = _llmOptions.PersistReasoningTrace ? reasoning : null,
            QualityScore = metrics?.QualityScore,
            CeremonyScore = metrics?.CeremonyScore,
            SimilarityToPrevious = metrics?.SimilarityToPrevious,
            NewArgumentCount = metrics?.NewArgumentCount,
            WordCount = metrics?.WordCount,
            HasFarewell = metrics?.HasFarewell,
            HasAgreement = metrics?.HasAgreement,
            HasDisagreement = metrics?.HasDisagreement,
            SelfSimilarity = metrics?.SelfSimilarity,
        };
        var cid = await merkleStore.PutBlobAsync(node);
        return cid.ToString();
    }

    private async Task<string> StoreSynthesisNodeAsync(int level, string content, float[] embedding,
                                      string d1Hash, string d2Hash,
                                      float variability, float resonance, string phase, string sessionId = "",
                                      string speakerId = "", string synthesisNodeType = "",
                                      float moderatorSelectionConfidence = 0f,
                                      Citation[]? authorityReferences = null,
                                      bool isSustained = false,
                                      float ascentScore = 0f,
                                      float? joinMediation = null,
                                      float? joinNovelty = null,
                                      float? irreducibleTension = null,
                                      string? remainingDissensus = null,
                                      float? repeatsPremise = null,
                                      string[]? unresolvedTensions = null,
                                      float? tensionIntensity = null)
    {
        var node = new DialecticalNode
        {
            NodeType = synthesisNodeType,
            SpeakerId = speakerId,
            Level = level,
            Content = content.StripReasoningTags(),
            Embedding = embedding,
            ParentHashes = [d1Hash, d2Hash],
            Timestamp = DateTime.UtcNow,
            CyclePhase = phase,
            VariabilityScore = variability,
            ResonanceScore = resonance,
            AscentScore = ascentScore,
            JoinMediation = joinMediation,
            JoinNovelty = joinNovelty,
            IrreducibleTension = irreducibleTension,
            RemainingDissensus = remainingDissensus,
            RepeatsPremise = repeatsPremise,
            UnresolvedTensions = unresolvedTensions,
            TensionIntensity = tensionIntensity,
            IsSecondaryCenter = false,
            CenterShiftScore = 0f,
            ParentPatternHash = null,
            SessionId = sessionId,
            // Plumb the moderator-selection confidence into the node so consumers
            // (frontend InspectorPanel, downstream FASTER queries) can surface it
            // without needing a separate query. Extra nodes annotate further
            // via their ModeratorSelectionConfidence; the base field is
            // only present on the synthesis node and is meaningful for modules
            // with a dynamic moderator selector (custom modules).
            ModeratorSelectionConfidence = moderatorSelectionConfidence,
            AuthorityReferences = authorityReferences,
            IsSustained = isSustained,
        };
        var cid = await merkleStore.PutBlobAsync(node);

        // First complete round on an empty corpus: the continuity reference is now owed, and
        // this node is the first evidence of the space the corpus is being built in. Capture
        // from the server at this point and the reference describes the corpus's own space -
        // which is the only space worth validating against. No-op on every later round.
        await TryCaptureEmbeddingSentinelAsync(cid.ToString(), node.Embedding, sessionId);

        return cid.ToString();
    }

    /// <summary>
    /// Resolves the embedding sentinel from the kernel and, if a capture is pending, takes it
    /// now that a round has completed. Best-effort: a capture failure must never fail the turn
    /// that produced the node.
    /// </summary>
    private async Task TryCaptureEmbeddingSentinelAsync(string cid, float[] embedding, string sessionId)
    {
        if (embedding.Length == 0)
            return;   // nothing was embedded this round; not a usable capture point

        try
        {
            var sentinel = kernel.Services.GetService(typeof(IEmbeddingSentinel)) as IEmbeddingSentinel;
            if (sentinel is null)
                return;

            await sentinel.TryCaptureReferenceAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Embedding continuity capture after storing {Cid} (session {SessionId}) failed; " +
                "will retry after the next round.",
                cid, sessionId);
        }
    }

    /// <summary>
    /// Stores the structured claims of an accepted synthesis as signed <see cref="ArgumentClaimNode"/>
    /// records - the S2 substrate. Each claim is anchored to the synthesis CID it came from
    /// (ParentHashes + SupportedBy provenance) so the claims graph derives from the existing
    /// Merkle spine rather than a parallel store. Tolerant: null/empty claim sets are a no-op,
    /// and a storage failure is logged without failing the dialogue turn.
    /// </summary>
    private async Task StoreClaimNodesAsync(
        string sessionId,
        int level,
        string synthesisCid,
        IReadOnlyList<ClaimDraft>? claims,
        CancellationToken ct)
    {
        if (claims == null || claims.Count == 0)
            return;

        var stored = 0;
        foreach (var claim in claims)
        {
            if (string.IsNullOrWhiteSpace(claim.Claim))
                continue;
            ct.ThrowIfCancellationRequested();
            try
            {
                var node = new ArgumentClaimNode
                {
                    SessionId = sessionId,
                    Level = level,
                    ClaimText = claim.Claim,
                    Confidence = Math.Clamp(claim.Confidence, 0f, 1f),
                    FalsifiedBy = claim.FalsifiedBy,
                    SupportedBy = claim.SupportedBy.Length > 0
                        ? claim.SupportedBy
                        : [synthesisCid],
                    Attacks = claim.AttackedBy,
                    ParentHashes = [synthesisCid],
                    SupportFromLabels = claim.SupportedBy.Length > 0 ? claim.SupportedBy : null,
                    Timestamp = DateTime.UtcNow,
                };
                await merkleStore.PutBlobAsync(node);
                stored++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Failed to store claim at level {Level} of session {SessionId} (non-fatal).",
                    level, sessionId);
            }
        }

        if (stored > 0)
            logger.LogInformation(
                "Stored {Stored}/{Total} claims for level {Level} of session {SessionId} (synthesis {Cid}).",
                stored, claims.Count, level, sessionId, synthesisCid);
    }

    private record AscensionResult(
        string Metaphor,
        string NextTheme,
        string ConnectiveSentence,
        string SynthesisThesis,
        bool Validated,
        int Attempts,
        List<string> RejectionReasons,
        string ModeratorName = "",
        float ModeratorSelectionConfidence = 0f,
        bool IsSustained = false,
        float AscentScore = 0f,
        float? JoinMediation = null,
        float? JoinNovelty = null,
        IReadOnlyList<ClaimDraft>? Claims = null,
        float IrreducibleTension = 0f,
        string? RemainingDissensus = null,
        float? RepeatsPremise = null,
        string[]? UnresolvedTensions = null,
        float? TensionIntensity = null,
        string? SustainedReason = null);

    private async Task<AscensionResult> SynthesizeAndGateAscensionAsync(
        string currentTheme,
        string rootTheme,
        string lastD1Text,
        string lastD2Text,
        string phase,
        int level,
        string sessionId,
        CancellationToken ct,
        string mediationInsights = "")
    {
        var embeddingGenerator = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
        var dialecticalModerator = _config.Moderator as IDialecticalModerator;
        var moderator = _config.Moderator;
        float moderatorConfidence = 0f;
        var selectorWasUsed = false;
        MetaphorResult? metaphorResult = null;
        if (moderator == null && _config.ModeratorSelector != null)
        {
            selectorWasUsed = true;
            var selection = _config.ModeratorSelector.SelectModerator(rootTheme, currentTheme);
            moderator = selection.Moderator;
            moderatorConfidence = selection.Confidence;
            logger.LogInformation(
                "Moderator selected: {Key} (winner score {Win:f3}, runner-up {Run:f3}, confidence {Conf:f3}).",
                selection.ModeratorKey, selection.WinnerScore, selection.RunnerUpScore, selection.Confidence);
        }
        if (moderator is null)
        {
            throw new InvalidOperationException(
                $"No moderator or moderator selector is configured for module '{_config.ModuleKey}'. " +
                "A moderator is required to produce a synthesis and next theme.");
        }

        string metaphor, synthesisThesis;
        var attempts = 0;
        var rejections = new List<string>();
        string? nextTheme = null;
        string? connectiveSentence = null;
        bool validated = true;
        var moderatorName = string.Empty;
        var isSustained = false;
        string? sustainedReason = null;
        IReadOnlyList<ClaimDraft>? claimsOut = null;

        if (dialecticalModerator != null)
        {
            if (moderator != null) moderatorName = GetModeratorName(moderator);
            else throw new InvalidParameterException("No moderator present after selection: ", null);
            var metaphorAttempts = 0;
            float metaphorResonance;
            bool domainRepeated;
            do
            {
                metaphorAttempts++;
                metaphorResult = await dialecticalModerator.SynthesizeMetaphorAsync(currentTheme, lastD1Text, lastD2Text, phase, _usedMetaphorDomains, ct);

                var metaphorEmb = (await embeddingGenerator.GenerateAsync([metaphorResult.Metaphor], cancellationToken: ct))[0].Vector.ToArray();
                metaphorResonance = ComputeResonance(metaphorEmb);

                domainRepeated = !string.IsNullOrWhiteSpace(metaphorResult.ImageDomain)
                    && _usedMetaphorDomains.Any(d => d.Equals(metaphorResult.ImageDomain, StringComparison.OrdinalIgnoreCase));

                if (metaphorResonance < options.SynthesisResonanceThreshold && !domainRepeated)
                    break;

                logger.LogWarning(
                    "Synthesis metaphor at level {Level} too similar to a prior one (resonance {R:F2}, domain \"{D}\" repeated={Repeated}); retrying ({Attempt}/{Max}).",
                    level, metaphorResonance, metaphorResult.ImageDomain, domainRepeated, metaphorAttempts, options.MaxSynthesisRetries + 1);
            }
            while (metaphorAttempts <= options.MaxSynthesisRetries);

            if (!string.IsNullOrWhiteSpace(metaphorResult.ImageDomain))
                _usedMetaphorDomains.Add(metaphorResult.ImageDomain);

            metaphor = metaphorResult.Metaphor;
            synthesisThesis = metaphorResult.Thesis;
            claimsOut = metaphorResult.Claims;

            logger.LogInformation(
                "Accepted synthesis at level {Level} (metaphor attempt {Attempt}/{Max}, resonance {R:F2}, domain \"{D}\", repeated={Repeated}).",
                level, metaphorAttempts, options.MaxSynthesisRetries + 1, metaphorResonance, metaphorResult.ImageDomain, domainRepeated);

            string? feedback = null;
            NextThemeResult candidate;
            var hadNonEmptyCandidate = false;
            var exhaustionConfirmed = false;
            string? exhaustionReason = null;
            var climbedThemes = await GetClimbedThemesAsync(level, sessionId);

            do
            {
                attempts++;
                candidate = await dialecticalModerator.SynthesizeNextThemeAsync(
                    metaphor, currentTheme, rootTheme, feedback, climbedThemes, synthesisThesis, ct);

                // The moderator may decline: it declares the rung spent rather than proposing a
                // candidate. The gate answers for that claim - same agent, different role, ladder
                // in view - because a moderator that finds declining cheaper than proposing would
                // otherwise end every inquiry early.
                if (candidate.IsExhausted)
                {
                    exhaustionReason = candidate.ExhaustionReason;
                    var exhaustionVerdict = await dialecticalModerator.EvaluateExhaustionAsync(
                        rootTheme, candidate.ExhaustionReason ?? string.Empty, climbedThemes, ct);

                    if (exhaustionVerdict.IsValidGeneralization)
                    {
                        exhaustionConfirmed = true;
                        validated = true;
                        logger.LogInformation(
                            "Ascension exhausted at level {Level}: the moderator declined to propose and the gate confirmed. {Reason}",
                            level, candidate.ExhaustionReason);
                    }
                    else
                    {
                        rejections.Add(exhaustionVerdict.Reasoning);
                        feedback = exhaustionVerdict.Reasoning;
                        logger.LogWarning(
                            "Ascension gate rejected the moderator's exhaustion claim at level {Level} " +
                            "(attempt {Attempt}/{Max}): {Reason}",
                            level, attempts, options.MaxAscensionGateRetries + 1, exhaustionVerdict.Reasoning);
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(candidate.Theme))
                {
                    validated = false;
                    feedback = "The previous response contained no question. Return a non-empty QUESTION line and a GENERALIZES line.";
                    rejections.Add("Moderator returned an empty next-theme candidate.");
                    logger.LogWarning(
                        "Moderator {Name} returned an empty next-theme candidate at level {Level} (attempt {Attempt}/{Max}); requesting a usable question.",
                        moderatorName, level, attempts, options.MaxAscensionGateRetries + 1);
                    continue;
                }

                hadNonEmptyCandidate = true;

                var verdict = await dialecticalModerator.EvaluateAscensionAsync(rootTheme, candidate.Theme, candidate.ConnectiveSentence, climbedThemes, ct);
                validated = verdict.IsValidGeneralization;

                if (!validated)
                {
                    rejections.Add(verdict.Reasoning);
                    feedback = verdict.Reasoning;
                    logger.LogWarning(
                        "Ascension gate rejected candidate theme at level {Level} (attempt {Attempt}/{Max}): \"{Theme}\" - {Reasoning}",
                        level, attempts, options.MaxAscensionGateRetries + 1, candidate.Theme, verdict.Reasoning);
                }
            }
            while (!validated && attempts <= options.MaxAscensionGateRetries);

            // Empty output is a generation failure, not evidence that the concept has
            // reached a fixed point. Try the structured synthesis path once before
            // classifying a genuinely rejected question as sustained.
            if (!validated && !hadNonEmptyCandidate)
            {
                var recoveredTheme = await DeriveFallbackNextThemeAsync(
                    moderator, metaphor, synthesisThesis, connectiveSentence ?? string.Empty, currentTheme, rootTheme, ct);
                if (!string.IsNullOrWhiteSpace(recoveredTheme))
                {
                    var recoveredVerdict = await dialecticalModerator.EvaluateAscensionAsync(
                        rootTheme, recoveredTheme, connectiveSentence ?? string.Empty, climbedThemes, ct);
                    attempts++;
                    if (recoveredVerdict.IsValidGeneralization)
                    {
                        candidate = new NextThemeResult(recoveredTheme, connectiveSentence ?? string.Empty);
                        validated = true;
                        logger.LogInformation(
                            "Recovered next theme at level {Level} after empty theme outputs: \"{Theme}\".",
                            level, recoveredTheme);
                    }
                    else
                    {
                        rejections.Add(recoveredVerdict.Reasoning);
                        logger.LogWarning(
                            "Recovered next theme was rejected at level {Level}: \"{Theme}\" - {Reasoning}",
                            level, recoveredTheme, recoveredVerdict.Reasoning);
                    }
                }
            }

            nextTheme = candidate.Theme;
            connectiveSentence = candidate.ConnectiveSentence;

            if (exhaustionConfirmed)
            {
                // The moderator declined and the gate confirmed: the inquiry is spent at this
                // rung. Hand the inquiry back rather than forcing an ascent.
                isSustained = true;
                nextTheme = string.Empty;
                connectiveSentence = string.Empty;
                sustainedReason = $"exhaustion_confirmed: {exhaustionReason}";
                logger.LogInformation(
                    "Ascension exhausted at level {Level}: the moderator declined to propose and the gate confirmed. Sustaining the current theme. {Reason}",
                    level, exhaustionReason);
            }

            if (!validated)
            {
                if (!selectorWasUsed || moderatorConfidence < options.SustainedConfidenceThreshold)
                {
                    isSustained = true;
                    sustainedReason =
                        $"gate_exhausted: the gate could not validate any candidate after {attempts} attempts";
                    nextTheme = string.Empty;
                    connectiveSentence = string.Empty;
                    logger.LogWarning(
                        "Ascension gate never validated a candidate at level {Level} after {Attempts} attempts " +
                        "(moderator confidence {Conf:F3} below threshold {Thresh:F3}); sustaining current theme instead of forcing ascent.",
                        level, attempts, moderatorConfidence, options.SustainedConfidenceThreshold);
                }
                else
                {
                    logger.LogWarning(
                        "Ascension gate never validated a candidate at level {Level} after {Attempts} attempts; " +
                        "proceeding with last candidate anyway: \"{Theme}\".",
                        level, attempts, nextTheme);
                }
            }
        }
        else if (moderator != null)
        {
            moderatorName = GetModeratorName(moderator);

            var responses = string.IsNullOrWhiteSpace(mediationInsights)
                ? new[] { new AgentResponse(lastD1Text), new AgentResponse(lastD2Text) }
                : new[] { new AgentResponse(lastD1Text), new AgentResponse(lastD2Text), new AgentResponse(mediationInsights) };
            var history = moderator.CreateSynthesisHistory(currentTheme, responses, "synthesis", phase);
            // Root-thesis anchoring: the synthesis contract demands claims
            // grounded in the ROOT question, so the moderator must actually see it. Prepend the
            // anchor above the role prompt; ModeratorBase rides it through SynthesizeAsync.
            if (!string.IsNullOrWhiteSpace(rootTheme) && history.Count > 0)
            {
                var anchor = $"The ROOT question is: {rootTheme}\n" +
                             $"You are synthesizing the thematic level: {currentTheme}.\n" +
                             "Every claim in your synthesis must link back to the ROOT question " +
                             "within one paragraph. If this level cannot be linked to the root, " +
                             "say so explicitly instead of drifting.";
                history[0] = new ChatMessageContent(history[0].Role, $"{anchor}\n\n{history[0].Content}");
            }

            var result = await moderator.SynthesizeAsync(history, ct);
            metaphorResult = result;          // markers below are read off this
            metaphor = result.Metaphor;
            synthesisThesis = result.Thesis;
            claimsOut = result.Claims;
            nextTheme = !string.IsNullOrWhiteSpace(result.NextTheme) ? result.NextTheme : string.Empty;
            connectiveSentence = !string.IsNullOrWhiteSpace(result.ConnectiveSentence) ? result.ConnectiveSentence : string.Empty;
            validated = true;

            logger.LogInformation(
                "Accepted synthesis at level {Level} (moderator {Name} synthesis, domain \"{D}\", nextTheme=\"{N}\").",
                level, moderatorName, result.ImageDomain, nextTheme);
        }
        else
        {
            metaphor = string.Empty;
            synthesisThesis = string.Empty;
            nextTheme = string.Empty;
            connectiveSentence = string.Empty;
        }

        // Guarantee a usable next theme whenever we are genuinely ascending (not
        // intentionally sustaining). If the moderator produced no next theme - a common
        // small-model failure - derive one rather than leave it empty, which would make
        // the next level re-ask the current/root question and create duplicate question
        // nodes (the "round 2 == round 1" symptom).
        if (moderator != null && !isSustained && string.IsNullOrWhiteSpace(nextTheme))
        {
            var fallback = await DeriveFallbackNextThemeAsync(moderator, metaphor, synthesisThesis, connectiveSentence, currentTheme, rootTheme, ct);
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                logger.LogWarning(
                    "Moderator {Name} produced no next theme at level {Level}; derived fallback next theme: \"{Theme}\".",
                    moderatorName, level, fallback);
                nextTheme = fallback;
                if (string.IsNullOrWhiteSpace(connectiveSentence) && fallback.Length > 0)
                    connectiveSentence = fallback;
            }
        }

        // Score how well the accepted synthesis mediates the thesis/antithesis poles and advances beyond both.
        // This is recorded on the node for downstream analysis; failures never block ascension.
        var ascentScore = 0f;
        float? joinMediation = null;
        float? joinNovelty = null;
        var irreducibleTension = 0f;
        if (!string.IsNullOrWhiteSpace(synthesisThesis))
        {
            try
            {
                var scorer = new AscentQualityScorer(
                    kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>(),
                    options.AscentScorerMode,
                    options.AscentBetweennessFloor);
                var quality = await scorer.ComputeAsync(currentTheme, lastD2Text, synthesisThesis, ct);
                if (quality is not null)
                {
                    ascentScore = quality.AscentScore;
                    joinNovelty = quality.JoinNovelty;
                    joinMediation = quality.MediationDeviation;
                    irreducibleTension = quality.IrreducibleTension;
                    logger.LogInformation(
                        "Level {Level} ascent quality: {Quality} (sustained={IsSustained}).",
                        level, quality, isSustained);

                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "Ascent-quality scoring skipped at level {Level}.", level);
            }
        }

        // Extract the moderator's negative-dialectic markers, if available.
        //
        // Read off metaphorResult, which BOTH synthesis paths must assign. The buffered path
        // assigns it in the metaphor loop above; the streaming path assigns it from the
        // SynthesizeAsync result. When only one path assigned it, the other silently produced
        // null markers for every synthesis - the model emitted the fields correctly and the
        // parser read them, but nothing carried them out of the branch.
        string? remainingDissensus = metaphorResult?.RemainingDissensus;
        float? repeatsPremise = metaphorResult?.RepeatsPremise;
        string[]? unresolvedTensions = MediationUnresolvedTensions.Count > 0 ? [.. MediationUnresolvedTensions] : null;
        float? tensionIntensity = LatestTensionIntensity > 0 ? LatestTensionIntensity : null;

        return new AscensionResult(metaphor, nextTheme, connectiveSentence, synthesisThesis, validated, attempts, rejections, moderatorName, moderatorConfidence, isSustained, ascentScore, joinMediation, joinNovelty, claimsOut, irreducibleTension, remainingDissensus, repeatsPremise, unresolvedTensions, tensionIntensity, SustainedReason: sustainedReason);
    }

    /// <summary>
    /// Produces a non-empty next-inquiry theme when the synthesis moderator omitted one.
    /// First re-prompts the moderator with a focused next-theme request; if that also
    /// fails, falls back to the generalization material (connective sentence, then thesis)
    /// the synthesis already produced. Never falls back to the current or root theme.
    /// </summary>
    private async Task<string?> DeriveFallbackNextThemeAsync(
        IModerator moderator,
        string metaphor,
        string thesis,
        string connectiveSentence,
        string currentTheme,
        string rootTheme,
        CancellationToken ct)
    {
        try
        {
            var connectiveBlock = string.IsNullOrWhiteSpace(connectiveSentence)
                ? string.Empty
                : $"\nConnective sentence from the synthesis: {connectiveSentence}";
            var prompt = $$"""
                You are the synthesis moderator for a dialectical inquiry.

                The root question: {{rootTheme}}
                The theme just debated: {{currentTheme}}

                Synthesis metaphor: {{metaphor}}
                Synthesis thesis: {{thesis}}{{connectiveBlock}}

                Propose the next inquiry theme - the next RUNG of the ascent - that naturally
                generalizes this synthesis one step further toward the root question. It must
                NOT restart or repeat the debated theme.

                OUTPUT: JSON with exactly these fields (no markdown, no code blocks):
                {"image_domain": "primary metaphor domain", "metaphor": "a brief metaphor for the next theme", "thesis": "the claim implied by the next theme", "next_theme": "the next inquiry theme", "connective_sentence": "one sentence showing how the next theme generalizes the root question"}
                """;

            var result = await moderator.SynthesizeAsync(new ChatHistory(prompt), ct);
            if (!string.IsNullOrWhiteSpace(result.NextTheme)) return result.NextTheme.Trim();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Fallback next-theme re-prompt failed; using deterministic fallback.");
        }

        var deterministic = FirstNonEmpty(connectiveSentence, thesis);
        return string.IsNullOrWhiteSpace(deterministic) ? null : deterministic.Trim();
    }

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate;
        }
        return string.Empty;
    }

    /// <summary>
    /// Builds an ascent-directive message that is prepended to the opposing
    /// dialectician's text before it is shown as the user turn. In the streaming
    /// path the directive includes the agent's own last quality metrics so pressure
    /// is tailored to specific weaknesses (e.g. self-repetition, lack of novelty).
    /// </summary>
    private string BuildAscentDirective(
        int level,
        int turn,
        string phase,
        ResponseQualityMetrics? lastMetrics = null,
        string? theme = null,
        EmotionalVolatilityState? evl = null,
        IDialectician? dialectician = null,
        double previousMomentum = 0.0)
    {
        var directive = "";

        if (options.AscentPressureEnabled)
        {
            if (turn == 0)
            {
                if (level == 0)
                {
                    directive += "This is the opening of the inquiry. Stake out your foundational thesis on the theme directly and concisely. Do NOT begin with generic pleasantries, ceremonial language, or an extended self-introduction - state your substantive argument immediately. ";
                }
                else
                {
                    directive += $"This is Round {level + 1} of the ascent, exploring the new rung: \"{theme}\". Do NOT repeat your foundational stance or role introduction. Use the prior synthesis as context; develop a fresh argument only where it is grounded, and identify limits rather than forcing novelty. ";
                }
            }
            else
            {
                directive += "Respond directly to the opposing argument. Develop a new angle only when it adds grounded substance; do not force novelty or a particular argumentative form. ";

                var speakerKey = dialectician is null
                    ? null
                    : ReferenceEquals(dialectician, _config.Dialectician1)
                        ? _config.Dialectician1SpeakerId
                        : ReferenceEquals(dialectician, _config.Dialectician2)
                            ? _config.Dialectician2SpeakerId
                            : dialectician.GetType().FullName;
                var suggestedMove = options.EnableMoveScheduler
                    ? MoveScheduler.Suggest(lastMetrics)
                    : null;
                if (suggestedMove.HasValue && speakerKey is not null && _moveSuggestionsIssued.Add(speakerKey))
                {
                    directive += $"OPTIONAL ANGLE TO CONSIDER: {MoveRotation.GetInstruction(suggestedMove.Value)} If it is not grounded or useful here, do not force it; identify what remains unresolved instead. ";
                }
            }

            if (phase == "emergence")
            {
                if (turn == 0)
                    directive += "Stake out your position with clarity and rigor. ";
                else
                    directive += "Deepen the conceptual contrast between your framework and the opponent's. ";
            }
            else if (phase == "growth")
            {
                directive += "The debate is productively advancing - build on emerging insights. ";
            }
            else if (phase == "renewal")
            {
                directive += "New ground is being broken - sustain this creative momentum. ";
            }
            else if (phase == "saturation")
            {
                directive += "The current framing may be yielding diminishing returns. Explore another perspective only if it follows from the arguments; otherwise identify what this framing cannot resolve. ";
            }
            else if (phase == "crisis")
            {
                directive += "The dialogue may have reached an impasse. Do not manufacture a breakthrough; name the unresolved issue and offer a reframe only if it is grounded. ";
            }

            // Quality-aware pressure: target specific weaknesses
            if (lastMetrics != null)
            {
                if (lastMetrics.SelfSimilarity > 0.35)
                    directive += "Your last response repeated arguments, examples, or phrasing. Avoid repeating them; if no grounded development is available, make the limit explicit. ";
                else if (lastMetrics.SimilarityToPrevious > options.SimilarityToPreviousHigh)
                    directive += "Your last response closely resembled the opposing speaker's. Clarify the substantive difference, or indicate where the positions overlap. ";

                if (lastMetrics.NewArgumentCount < 1)
                    directive += "Your last response introduced no new arguments. Avoid restatement; clarify a grounded distinction if useful, or state what remains unresolved. ";
                if (lastMetrics.CeremonyScore > options.CeremonyScoreHigh)
                    directive += "Eliminate conversational courtesies, concessions ('While I respect...', 'While you raise valid points...'), or praise. Attack the substance directly. ";
                if (lastMetrics.HasFarewell)
                    directive += "Do not conclude your response as if the dialogue is over. ";
            }

            // Emotional Volatility Layer: inject a role-specific tonal calibration phrase
            // inside the private [REASONING] guidance when the EVL signal is significant.
            // This shapes the register of the [RESPONSE] without ever appearing in it.
            if (evl != null && evl.IsSignificant(previousMomentum) && dialectician != null)
            {
                var affectPhrase = dialectician.BuildAffectInstruction(evl);
                if (!string.IsNullOrWhiteSpace(affectPhrase))
                    directive += $"\n\nTONAL CALIBRATION (private [REASONING] only - never leak into [RESPONSE]): {affectPhrase} ";
            }
        }

        return directive.Trim();
    }
}

