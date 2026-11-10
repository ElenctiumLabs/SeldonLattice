using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Application.Audit;
using SeldonLattice.Application.Extensions;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Agents;
using SeldonLattice.Domain.Models.Cycle;
using SeldonLattice.Domain.Models.Results;
using SeldonLattice.Domain.Options;

namespace SeldonLattice.Application.Agents.Moderators;

/// <summary>
/// Abstract base for all moderator agents.
///
/// Provides:
/// <list type="bullet">
///   <item><see cref="GenerateInterjectionAsync"/> - unified stalemate interjection using <see cref="BuildInterjectionPrompt"/>.</item>
///   <item><see cref="SynthesizeAsync"/> - LLM call delegating to <see cref="ParseJsonSynthesisResponse"/>.</item>
///   <item><see cref="ParseJsonSynthesisResponse"/> - parses the standard JSON synthesis schema.</item>
///   <item><see cref="ParseTextSynthesisResponse"/> - parses the DOMAIN/THESIS/METAPHOR text schema.</item>
///   <item><see cref="ParseNextThemeResult"/> - parses the GENERALIZES: text schema for next-theme proposals.</item>
///   <item><see cref="ParseAscensionVerdict"/> - parses the {"valid":…,"reasoning":…} JSON gate verdict.</item>
///   <item><see cref="CallLlmAsync"/> - single-message LLM call helper.</item>
///   <item><see cref="StripCodeFences"/> - strips ```…``` markdown fences from LLM output.</item>
///   <item><see cref="SanitizeTheme"/> / <see cref="SanitizeConnective"/> - clean LLM text output.</item>
/// </list>
///
/// Subclasses supply <see cref="FallbackResult"/>, <see cref="FallbackImageDomain"/>,
/// <see cref="CreateSynthesisHistory"/>, and <see cref="BuildInterjectionPrompt"/>.
/// </summary>
public abstract class ModeratorBase(Kernel kernel, IOptions<DialecticsOptions> options, IProviderRateLimiter rateLimiter) : IModerator
{
    private readonly DialecticsOptions _options = options.Value;

    /// <summary>
    /// Logger resolved from the kernel, so subclasses keep their existing constructor shape.
    /// Null-safe: a kernel built without logging simply loses the warning.
    /// </summary>
    private readonly ILogger? logger = kernel.LoggerFactory.CreateLogger("SeldonLattice.Moderators");
    /// <summary>Hard-coded result returned when the LLM response cannot be parsed.</summary>
    protected abstract MetaphorResult FallbackResult { get; }

    /// <summary>Default image-domain string when the <c>image_domain</c> JSON field is absent or empty.</summary>
    protected abstract string FallbackImageDomain { get; }

    // IModerator contract - role prompt left to each subclass.
    public abstract ChatHistory CreateSynthesisHistory(
        string theme,
        AgentResponse[] previousResponses,
        string moderationFocus,
        string cyclePhase = "growth");

    // -- Interjection ----------------------------------------------------------

    /// <summary>
    /// Builds the role-specific interjection prompt for a detected stalemate.
    /// Receives the signal type, the debate theme, pre-truncated snippets of each
    /// agent's last response, and their display names.
    /// The returned string is sent as a single user message by <see cref="GenerateInterjectionAsync"/>.
    /// </summary>
    protected abstract string BuildInterjectionPrompt(
        StalemateSignal signal,
        string theme,
        string d1Snippet,
        string d2Snippet,
        string d1Name,
        string d2Name);

    /// <summary>
    /// Generates a targeted interjection in the moderator's own role to break
    /// a detected stalemate. Truncates snippets, calls <see cref="BuildInterjectionPrompt"/>,
    /// then <see cref="CallLlmAsync"/>. Subclasses only need to supply the prompt.
    /// </summary>
    public virtual async Task<string> GenerateInterjectionAsync(
        StalemateSignal signal,
        string theme,
        string dialectician1Text,
        string dialectician2Text,
        string d1Name,
        string d2Name,
        CancellationToken ct = default)
    {
        var snippetMaxLength = _options.InterjectionSnippetMaxLength;
        var d1Snippet = dialectician1Text.Length > snippetMaxLength
            ? dialectician1Text[..snippetMaxLength] + "…"
            : dialectician1Text;
        var d2Snippet = dialectician2Text.Length > snippetMaxLength
            ? dialectician2Text[..snippetMaxLength] + "…"
            : dialectician2Text;

        var prompt = BuildInterjectionPrompt(signal, theme, d1Snippet, d2Snippet, d1Name, d2Name);
        return (await CallLlmAsync(prompt, ct)).Trim();
    }

    // -- LLM call helper ------------------------------------------------------

    /// <summary>
    /// Sends a single user message to the LLM and returns the raw response content.
    /// Eliminates the repeated new-ChatHistory / GetRequired / GetChatMessage pattern.
    /// Enforces per-provider rate limits before each LLM call.
    /// </summary>
    protected async Task<string> CallLlmAsync(string userMessage, CancellationToken ct = default)
    {
        await rateLimiter.WaitForSlotAsync(ct);

        // The parsing contract is placed above the prompt text so any moderator flow
        // (interjection, metaphor, next-theme, ascension gate) is told up front that its
        // reply is processed verbatim and that preamble/sign-offs are content. Skipped
        // when the gate is off (small local models ignore it).
        var history = new ChatHistory();
        history.AddUserMessage(
            PromptContracts.Enabled
                ? $"{PromptContracts.OutputContract}\n\n{userMessage}"
                : userMessage);
        var chatCompletion = kernel.GetRequiredService<IChatCompletionService>();
        var response = await chatCompletion.GetChatMessageContentAsync(history, cancellationToken: ct);
        return response.Content ?? string.Empty;
    }

    // -- IModerator: SynthesizeAsync ------------------------------------------

    /// <summary>Sends <paramref name="history"/> to the LLM and parses the JSON synthesis response.
    /// Enforces per-provider rate limits before the LLM call.</summary>
    public virtual async Task<MetaphorResult> SynthesizeAsync(ChatHistory history, CancellationToken ct = default)
    {
        const int maxAttempts = 3;

        // Prepend the parsing contract (and, when enabled, the grounded-synthesis claims
        // mandate) above the role's first message so models know the reply must be a
        // bare JSON object with refutable claims before they read the prompt. The role
        // stays right after it. The first message may be System or User depending on the
        // moderator subclass, so the contracts are wrapped with the same role rather than
        // forced to System.
        var effectiveHistory = new ChatHistory();
        var prependContract = PromptContracts.Enabled
            ? $"{PromptContracts.OutputContract}\n\n{PromptContracts.ReasonableSynthesisContract}"
            : null;
        for (var i = 0; i < history.Count; i++)
        {
            var message = history[i];
            if (i == 0 && prependContract != null)
                effectiveHistory.Add(new ChatMessageContent(message.Role, $"{prependContract}\n\n{message.Content}"));
            else
                effectiveHistory.Add(message);
        }

        // Small local models frequently return plain text instead of the requested JSON.
        // Without a retry, a text reply is silently replaced by FallbackResult and every
        // dissensus marker (remaining_dissensus, self_check.repeats_premise) is null - the
        // experiment cannot run on null markers.
        //
        // The retry keys off TryParseJsonSynthesisResponse, NOT ParseJsonSynthesisResponse.
        // The latter swallows every parse failure and returns FallbackResult, so a
        // try/catch around it can never observe a failure: a malformed reply would return
        // canned text from the first attempt and no retry would ever happen.
        var chatCompletion = kernel.GetRequiredService<IChatCompletionService>();

        // Constrain the decoder to the synthesis schema so the marker keys are present even
        // when the model would otherwise drop them. See SynthesisSchema for the measurement.
        var executionSettings = new PromptExecutionSettings
        {
            ExtensionData = new Dictionary<string, object>
            {
                ["response_format"] = SynthesisSchema.ResponseFormat,
            },
        };

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // Rate limit every attempt. This call used to sit above the loop; moving it
            // inside keeps the retry path throttled too, so a model that reliably fails to
            // emit JSON cannot turn one synthesis into three unthrottled calls.
            await rateLimiter.WaitForSlotAsync(ct);

            var chatMessage = await chatCompletion.GetChatMessageContentAsync(
                effectiveHistory.EnsureAtLeastOneUserMessage(), executionSettings, cancellationToken: ct);
            var content = chatMessage.Content ?? string.Empty;

            if (TryParseJsonSynthesisResponse(content, out var parsed))
                return parsed;

            if (attempt < maxAttempts)
            {
                if (logger != null)
                logger.LogWarning(
                    "Synthesis response was not valid JSON (attempt {Attempt}/{MaxRetries}). Retrying with feedback. Raw head: {RawHead}",
                    attempt, maxAttempts, content.Length > 300 ? content[..300] : content);

                // Show the model its own reply and the exact shape required. The example is
                // built by serialising a real object rather than interpolated by hand, so
                // the braces shown to the model cannot drift from the schema.
                effectiveHistory.AddAssistantMessage(content);
                effectiveHistory.AddUserMessage(
                    "That response was not valid JSON. Output ONLY a single JSON object and nothing else - " +
                    "no markdown, no code fences, no text before or after it. Start with '{' and end with '}'. " +
                    $"Example: {JsonSynthesisExample}. Try again.");
            }
        }

		// Every attempt failed to parse. Log which one we are falling back on and return the
		// canned result, matching the single-shot behaviour callers already expect.

		if (logger != null)
			logger.LogWarning(
            "Synthesis produced no parseable JSON in {MaxAttempts} attempts; using FallbackResult.",
            maxAttempts);
        return FallbackResult;
    }

    /// <summary>
    /// The minimal JSON shape shown to the model when asking it to retry. Serialised from an
    /// anonymous object so it always matches the field names the parser reads.
    /// </summary>
    private static readonly string JsonSynthesisExample = JsonSerializer.Serialize(new
    {
        image_domain = "domain",
        metaphor = "the synthesis",
        thesis = "one sentence",
        next_theme = "next question",
        connective_sentence = "connects to root",
        // Placeholder text, not a copyable value. A concrete example here is echoed back
        // verbatim: the model returned repeats_premise 0.0 on every live synthesis while
        // this example read "what remains unresolved, or empty" with a literal 0.0, and
        // the phrase "or empty" licensed the empty remaining_dissensus that followed.
        remaining_dissensus = "the specific tension this synthesis leaves unresolved",
        self_check = new { repeats_premise = "<0.0-1.0>" }
    });

    // -- Parse helpers --------------------------------------------------------

    /// <summary>
    /// Parses {"image_domain","metaphor","thesis","next_theme","connective_sentence"}.
    ///
    /// Infallible by contract: a payload that cannot be parsed yields <see cref="FallbackResult"/>
    /// rather than throwing. Callers that need to know a parse failed - the synthesis retry
    /// loop, which must distinguish "the model answered" from "the model answered unusably" -
    /// call <see cref="TryParseJsonSynthesisResponse"/> instead.
    /// </summary>
    protected MetaphorResult ParseJsonSynthesisResponse(string jsonContent)
        => TryParseJsonSynthesisResponse(jsonContent, out var parsed) ? parsed! : FallbackResult;

    /// <summary>
    /// Attempts to parse a synthesis payload, reporting failure to the caller instead of
    /// substituting <see cref="FallbackResult"/>. Returns false when the payload holds no
    /// parseable JSON object.
    /// </summary>
    private bool TryParseJsonSynthesisResponse(string jsonContent, [NotNullWhen(true)] out MetaphorResult? result)
    {
        result = null;
        try
        {
            if (string.IsNullOrWhiteSpace(jsonContent))
                return false;

            var cleaned = StripCodeFences(jsonContent);

            // Models frequently emit TWO concatenated objects rather than one merged object:
            // the five synthesis fields first, then a second object carrying "claims" (and
            // "remaining_dissensus" / "self_check") because the grounded-synthesis mandate is
            // presented as a separate block. JsonDocument.Parse requires exactly one value and
            // throws "Extra data" on the second, which the catch below used to turn into a
            // silent fallback - so every such synthesis was discarded and replaced with canned
            // text. Merge the objects instead of rejecting them.
            //
            // This call is also the success test: it throws when the payload contains no
            // object at all, which is the only case where FallbackResult is the right answer.
            var root = ParsePossiblyConcatenatedObjects(cleaned);

            var imageDomain = root.TryGetProperty("image_domain", out var id) && id.GetString() is { Length: > 0 } idStr
                ? idStr : FallbackImageDomain;
            var metaphor  = root.TryGetProperty("metaphor",            out var m)  ? m.GetString()  ?? FallbackResult.Metaphor : FallbackResult.Metaphor;
            var thesis    = root.TryGetProperty("thesis",              out var t)  ? t.GetString()  ?? FallbackResult.Thesis   : FallbackResult.Thesis;
            var nextTheme = root.TryGetProperty("next_theme",          out var nt) ? nt.GetString() ?? string.Empty            : string.Empty;
            var connective= root.TryGetProperty("connective_sentence", out var cs) ? cs.GetString() ?? string.Empty            : string.Empty;

            // Structured claims channel: optional - the synthesis remains valid
            // prose without it, but when `claims` are present they ride out for merkle
            // storage. Tolerant parser; a malformed claims array is simply dropped.
            IReadOnlyList<ClaimDraft>? claims = null;
            if (root.TryGetProperty("claims", out var claimsElement) &&
                ClaimsContractParser.TryParseClaims(claimsElement, out var drafts))
                claims = drafts;

            // Negative-dialectic markers: the model's own report of what the synthesis
            // failed to close (remaining_dissensus) and how much it merely restated
            // the question (self_check.repeats_premise). Both are optional; null when
            // absent or unparseable.
            string? remainingDissensus = null;
            if (root.TryGetProperty("remaining_dissensus", out var rd) &&
                rd.GetString() is { Length: > 0 } rdStr)
                remainingDissensus = rdStr;

            float? repeatsPremise = null;
            if (root.TryGetProperty("self_check", out var sc) &&
                sc.TryGetProperty("repeats_premise", out var rp) &&
                rp.TryGetSingle(out var rpVal))
                repeatsPremise = rpVal;

            result = new MetaphorResult(metaphor, imageDomain, thesis, nextTheme, connective, claims,
                remainingDissensus, repeatsPremise);
            return true;
        }
        catch (Exception ex)
        {
			// Was a bare `catch { return FallbackResult; }`. That made every parse failure
			// indistinguishable from a legitimate answer: 8 of 10 syntheses in one validation batch
			// session were silently canned text, and the only way to notice was to diff the
			// stored nodes. Log it, with the head of the offending payload.

			if (logger != null)
				logger.LogWarning(ex,
                "Synthesis response failed to parse as JSON; using FallbackResult. Raw head: {RawHead}",
                jsonContent.Length > 300 ? jsonContent[..300] : jsonContent);
            result = null;
            return false;
        }
    }

    /// <summary>
    /// Parses a synthesis payload, tolerating the common model habit of emitting several
    /// concatenated top-level JSON objects instead of one merged object. Fields from later
    /// objects are merged into the first; earlier keys win on conflict, so the primary
    /// synthesis object takes precedence over anything repeated later.
    ///
    /// Returns a detached <see cref="JsonElement"/> so callers keep using TryGetProperty.
    /// </summary>
    protected static JsonElement ParseConcatenatedForTest(string cleaned) => ParsePossiblyConcatenatedObjects(cleaned);

    private static JsonElement ParsePossiblyConcatenatedObjects(string cleaned)
    {
        // Fast path: a single well-formed object.
        try
        {
            using var single = JsonDocument.Parse(cleaned);
            return single.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Fall through to the tolerant path.
        }

        // Models produce several near-miss shapes. the one observed in production is a well-formed object followed by ORPHANED KEY-VALUE PAIRS:
        //
        //   {"image_domain": ..., "metaphor": ...}      <- closes early
        //   "claims": [ ... ],                          <- no opening brace
        //   "remaining_dissensus": "..."
        //
        // The model merges the contract's separate `claims` block into the same reply but
        // forgets the second brace. Re-wrapping the tail in braces recovers it, and the
        // resulting shape also covers the concatenated-object case.
        var normalised = NormaliseOrphanedTail(cleaned);

        var merged = new System.Text.Json.Nodes.JsonObject();
        var bytes = System.Text.Encoding.UTF8.GetBytes(normalised);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
            // Without this the reader throws '{' is invalid after ... on a SECOND top-level
            // value, which is the concatenated-object shape.
            AllowMultipleValues = true,
        });

        var found = 0;
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                continue;

            using var doc = JsonDocument.ParseValue(ref reader);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!merged.ContainsKey(prop.Name))
                    merged[prop.Name] = System.Text.Json.Nodes.JsonNode.Parse(prop.Value.GetRawText());
            }
            found++;
        }

        if (found == 0 || merged.Count == 0)
            throw new JsonException("No JSON object found in synthesis response.");

        using var mergedDoc = JsonDocument.Parse(merged.ToJsonString());
        return mergedDoc.RootElement.Clone();
    }

    /// <summary>
    /// Repairs the "closed early, then more keys" shape by wrapping the orphaned tail in
    /// braces so it parses as a second object. A no-op when the payload is already a
    /// sequence of complete objects.
    /// </summary>
    private static string NormaliseOrphanedTail(string cleaned)
    {
        // Find the end of the first complete top-level object.
        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(cleaned);
            var reader = new Utf8JsonReader(bytes,
                new JsonReaderOptions { AllowTrailingCommas = true });
            var depth = 0;
            var firstObjectEndBytes = -1;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartObject) depth++;
                else if (reader.TokenType == JsonTokenType.EndObject)
                {
                    depth--;
                    if (depth == 0)
                    {
                        firstObjectEndBytes = (int)reader.BytesConsumed;
                        break;
                    }
                }
            }

            if (firstObjectEndBytes <= 0 || firstObjectEndBytes >= bytes.Length)
                return cleaned;

            // BytesConsumed is a BYTE offset; slicing a string needs a CHAR offset. The real
            // payload contains em-dashes and curly quotes, so the two diverge and slicing on
            // the byte count lands mid-string. Convert before slicing.
            var firstObjectEnd = System.Text.Encoding.UTF8
                .GetString(bytes, 0, firstObjectEndBytes).Length;

            var head = cleaned[..firstObjectEnd];
            var tail = cleaned[firstObjectEnd..].Trim();

            // Tail must start with a quoted key and contain a colon to be orphaned pairs.
            if (tail.Length > 0 && tail[0] == '"' && tail.Contains(':'))
                return $"{head}\n{{{tail}}}";

            return cleaned;
        }
        catch (JsonException)
        {
            return cleaned;
        }
    }

    /// <summary>Parses DOMAIN/THESIS/DISSENSUS/REPEATS-PREMISE/METAPHOR plain-text schema.</summary>
    protected static MetaphorResult ParseTextSynthesisResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new MetaphorResult(string.Empty, string.Empty, string.Empty);

        var domainIdx   = raw.IndexOf("DOMAIN:",   StringComparison.OrdinalIgnoreCase);
        var thesisIdx   = raw.IndexOf("THESIS:",   StringComparison.OrdinalIgnoreCase);
        var metaphorIdx = raw.IndexOf("METAPHOR:", StringComparison.OrdinalIgnoreCase);
        var dissensusIdx = raw.IndexOf("REMAINING DISSENSUS:", StringComparison.OrdinalIgnoreCase);
        var repeatsIdx = raw.IndexOf("REPEATS PREMISE:", StringComparison.OrdinalIgnoreCase);

        // DOMAIN and THESIS carry the structured claim, so both are required and in order.
        if (domainIdx < 0 || thesisIdx < 0 || domainIdx >= thesisIdx)
            return new MetaphorResult(raw, string.Empty, string.Empty);

        // Small models drop the METAPHOR label and leave the prose unlabelled. Re-insert the
        // label at the first unlabelled line so the schema below parses as written; without this
        // the whole labelled reply became the metaphor (the 2026-11-09 failure).
        if (metaphorIdx < 0)
        {
            var relabelled = ReinsertMetaphorLabel(raw, thesisIdx, dissensusIdx, repeatsIdx);
            if (relabelled is not null)
                return ParseTextSynthesisResponse(relabelled);
        }

        if (metaphorIdx < 0 || metaphorIdx < thesisIdx)
            return new MetaphorResult(raw, string.Empty, string.Empty);

        var dissensus = dissensusIdx >= 0 && dissensusIdx < metaphorIdx
            ? raw[(dissensusIdx + "REMAINING DISSENSUS:".Length)..(repeatsIdx > dissensusIdx ? repeatsIdx : metaphorIdx)].Trim()
            : null;
        if (string.Equals(dissensus, "none", StringComparison.OrdinalIgnoreCase)) dissensus = null;
        float? repeats = null;
        if (repeatsIdx >= 0 && repeatsIdx < metaphorIdx)
        {
            var repeatsText = raw[(repeatsIdx + "REPEATS PREMISE:".Length)..metaphorIdx].Trim();
            if (float.TryParse(repeatsText, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                repeats = Math.Clamp(parsed, 0f, 1f);
        }

        return new MetaphorResult(
            raw[(metaphorIdx + "METAPHOR:".Length)..].Trim(),
            raw[(domainIdx   + "DOMAIN:".Length)..thesisIdx].Trim(),
            raw[(thesisIdx   + "THESIS:".Length)..(dissensusIdx > thesisIdx ? dissensusIdx : repeatsIdx > thesisIdx ? repeatsIdx : metaphorIdx)].Trim(),
            RemainingDissensus: dissensus,
            RepeatsPremise: repeats);
    }

    /// <summary>
    /// Re-inserts a missing METAPHOR label at the first unlabelled line after the last recognised
    /// one. Returns null when the reply carries no unlabelled prose to attach.
    /// </summary>
    private static string? ReinsertMetaphorLabel(string raw, int thesisIdx, int dissensusIdx, int repeatsIdx)
    {
        var lastLabel = Math.Max(thesisIdx, Math.Max(dissensusIdx, repeatsIdx));
        if (lastLabel < 0) return null;

        var lineEnd = raw.IndexOfAny(['\r', '\n'], lastLabel);
        if (lineEnd < 0) return null;

        var start = lineEnd;
        while (start < raw.Length && char.IsWhiteSpace(raw[start])) start++;
        if (start >= raw.Length) return null;

        return string.Concat(raw.AsSpan(0, start), "METAPHOR: ", raw.AsSpan(start));
    }

    /// <summary>Parses &lt;question&gt;GENERALIZES: &lt;connective sentence&gt; schema.</summary>
    protected NextThemeResult ParseNextThemeResult(string raw)
    {
        const string ExhaustedMarker = "EXHAUSTED";

        if (string.IsNullOrWhiteSpace(raw))
            return new NextThemeResult(string.Empty, string.Empty);

        // The moderator may decline to propose at all: it declares the rung spent instead of
        // offering a candidate. That is a claim the ascension gate must answer for, not a
        // candidate to be judged as one. The marker must be a standalone word, so a question
        // beginning "Exhausted by the inquiry..." is never mistaken for a decline.
        var trimmedStart = raw.TrimStart();
        if (trimmedStart.StartsWith("EXHAUSTED", StringComparison.OrdinalIgnoreCase)
            && (trimmedStart.Length == ExhaustedMarker.Length
                || char.IsWhiteSpace(trimmedStart[ExhaustedMarker.Length])
                || trimmedStart[ExhaustedMarker.Length] == ':'))
        {
            var reason = Regex.Replace(trimmedStart, @"^EXHAUSTED[:\s]*", "", RegexOptions.IgnoreCase).Trim();
            return new NextThemeResult(
                string.Empty,
                string.Empty,
                IsExhausted: true,
                ExhaustionReason: string.IsNullOrWhiteSpace(reason) ? "The moderator gave no reason." : SanitizeConnective(reason));
        }

        const string marker = "GENERALIZES:";
        var idx = raw.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        // Prefer explicit labels so a model that reverses the two lines still produces
        // a usable candidate instead of an empty theme.
        var lines = raw.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var questionLine = lines.FirstOrDefault(line =>
            line.StartsWith("QUESTION:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("QUESTION ", StringComparison.OrdinalIgnoreCase));
        var generalizesLine = lines.FirstOrDefault(line =>
            line.StartsWith(marker, StringComparison.OrdinalIgnoreCase));
        if (questionLine is not null)
        {
            var question = Regex.Replace(questionLine, @"^QUESTION[:\s]*", "", RegexOptions.IgnoreCase);
            var connective = generalizesLine is null
                ? string.Empty
                : Regex.Replace(generalizesLine, @"^GENERALIZES[:\s]*", "", RegexOptions.IgnoreCase);
            return new NextThemeResult(SanitizeTheme(question), SanitizeConnective(connective));
        }

        return idx >= 0
            ? new NextThemeResult(SanitizeTheme(raw[..idx]), SanitizeConnective(raw[(idx + marker.Length)..].Trim()))
            : new NextThemeResult(SanitizeTheme(raw), string.Empty);
    }

    /// <summary>Parses {"valid": true|false, "reasoning": "…"}. Returns safe deny on failure.</summary>
    protected static AscensionVerdict ParseAscensionVerdict(string raw)
    {
        var text = StripCodeFences(raw.Trim());
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var valid     = root.TryGetProperty("valid",     out var v) && v.ValueKind == JsonValueKind.True;
            var reasoning = root.TryGetProperty("reasoning", out var r) ? r.GetString() ?? string.Empty : string.Empty;
            return new AscensionVerdict(valid, reasoning);
        }
        catch (JsonException)
        {
            return new AscensionVerdict(false, "Gate response could not be parsed; ascension denied for safety.");
        }
    }

    /// <summary>Strips leading/trailing ``` code fences from LLM output.</summary>
    private static string StripCodeFences(string raw)
    {
        var text = raw.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal)) return text;
        var nl = text.IndexOf('\n');
        text = nl >= 0 ? text[(nl + 1)..] : text;
        if (text.EndsWith("```", StringComparison.Ordinal)) text = text[..^3].Trim();
        return text;
    }

    /// <summary>Cleans a raw LLM theme: strips quotes, bullets, labels; truncates to first sentence.</summary>
    private string SanitizeTheme(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var text = raw.Trim();
        text = Regex.Replace(text, @"^[""']|[""']$", "");
        text = Regex.Replace(text, @"^[\s]*[-*•·●◦]+\s*", "");
        text = Regex.Replace(text, @"^(Question|Theme|Next question|Next theme|Inquiry|Probing question)[:\s]*", "", RegexOptions.IgnoreCase);
        var first = FirstSentenceWithTerminator(text);
        // A non-positive cap means "no limit". Treating 0 as a real length would
        // truncate every theme to the empty string and silently break the ascent.
        var maxLen = _options.ModeratorThemeMaxLength;
        return maxLen > 0 && first.Length > maxLen ? first[..maxLen] : first;
    }

    /// <summary>
    /// Returns the first sentence of <paramref name="text"/>, terminating punctuation included.
    /// Themes are questions: dropping the "?" stored every ascended rung as a statement.
    /// </summary>
    private static string FirstSentenceWithTerminator(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '.' or '!' or '?')
                return text[..(i + 1)].Trim();
            if (c is '\n' or '\r')
                return text[..i].Trim();
        }
        return text;
    }

    /// <summary>Cleans a raw LLM connective sentence: strips quotes; truncates to first line.</summary>
    private string SanitizeConnective(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var text = raw.Trim();
        text = Regex.Replace(text, @"^[""']|[""']$", "");
        var first = text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? text;
        // A non-positive cap means "no limit"; see SanitizeTheme.
        var maxLen = _options.ModeratorConnectiveMaxLength;
        return maxLen > 0 && first.Length > maxLen ? first[..maxLen] : first;
    }
}
