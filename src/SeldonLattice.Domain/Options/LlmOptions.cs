namespace SeldonLattice.Domain.Options;

/// <summary>
/// Configuration options for LLM service behavior (retry logic, token limits, temperature).
/// Read from "HttpClient:Llm" section in appsettings.json.
/// </summary>
public class LlmOptions
{
    /// <summary>
    /// Default maximum tokens for chat completion requests when not specified by the caller.
    /// Replaces the hardcoded value of 2048 in OpenAICompatibleChatCompletionService.
    /// Default is 2048.
    /// </summary>
    public int DefaultMaxTokens { get; set; }

    /// <summary>
    /// Default temperature for chat completion requests when not specified by the caller.
    /// Lower values (0.3–0.5) produce more consistent, structured philosophical reasoning.
    /// Default is 0.4.
    /// </summary>
    public double DefaultTemperature { get; set; } 

    /// <summary>
    /// Nucleus sampling parameter. Limits token selection to the top-P probability mass.
    /// 0.9 gives diverse but coherent outputs. Set to 1.0 to disable.
    /// Default is 0.9.
    /// </summary>
    public double TopP { get; set; }

    /// <summary>
    /// Penalty applied to tokens that have already appeared in the context.
    /// Values above 1.0 discourage repetition - helpful for preventing circular
    /// reasoning in long dialectical exchanges. Default is 1.1.
    /// </summary>
    public double RepeatPenalty { get; set; }

    /// <summary>
    /// Maximum number of retry attempts for transient LLM errors (500s, model unloaded).
    /// Replaces the hardcoded value of 3 in OpenAICompatibleChatCompletionService.
    /// Default is 3.
    /// </summary>
    public int MaxRetries { get; set; }

    /// <summary>
    /// Delay in milliseconds between retry attempts.
    /// Replaces the hardcoded value of 2000 in OpenAICompatibleChatCompletionService.
    /// Default is 2000.
    /// </summary>
    public int RetryDelayMs { get; set; }

    /// <summary>
    /// Whether the model may emit a separate reasoning/thinking channel before its answer.
    /// When false, the chat request sends
    /// <c>chat_template_kwargs: {"enable_thinking": false}</c>, which reasoning-capable
    /// templates (e.g. Gemma) honour by answering directly.
    ///
    /// Why this matters: a reasoning model bills its thinking against max_tokens and returns
    /// it in <c>message.reasoning_content</c> rather than <c>message.content</c>. Measured on
    /// gemma-4-E2B, a one-word answer consumed 290 completion tokens of thinking. At a
    /// max_tokens of 1024 a synthesis call is therefore truncated to
    /// <c>finish_reason=length</c> with an EMPTY content string, and the JSON parser then
    /// fails on empty input.
    ///
    /// Set false for reasoning-capable local models. Harmless for models whose template
    /// ignores the kwarg. Default is true (preserve existing behaviour).
    /// </summary>
    public bool EnableThinking { get; set; } = true;

    /// <summary>
    /// Whether to persist the model's reasoning/thinking trace alongside each turn's output.
    /// When true, the <c>reasoning_content</c> returned by reasoning-capable models (e.g. Gemma)
    /// is stored on the <see cref="SeldonLattice.Domain.Models.Storage.DialecticalNode"/> as
    /// <c>ReasoningTrace</c>. This is the model's deliberation record - what it considered,
    /// planned, and rejected before producing its answer.
    ///
    /// Requires <see cref="EnableThinking"/> to be true. Cost: thinking increases token usage
    /// ~8× and latency ~4–6× per turn (measured on gemma-4-E2B: 70->577 tokens, ~3s->12–19s).
    /// Default is false.
    /// </summary>
    public bool PersistReasoningTrace { get; set; } = false;
}
