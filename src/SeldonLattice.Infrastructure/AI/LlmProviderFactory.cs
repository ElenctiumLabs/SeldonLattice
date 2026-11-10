using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using OpenAI;
using System.ClientModel;

namespace SeldonLattice.Infrastructure.AI;

/// <summary>
/// Factory for configuring LLM providers (chat and embedding) on the Semantic Kernel builder.
/// Supports Ollama (default), Groq, Grok, OpenCodeZen, and Gemini (Google AI Studio) providers.
/// </summary>
public static class LlmProviderFactory
{
    /// <summary>
    /// Configures the Semantic Kernel builder with chat and embedding services based on configuration.
    /// Supports Ollama (local), Groq (cloud), Grok (cloud), OpenCodeZen (cloud), and Gemini (Google AI Studio, cloud).
    /// </summary>
    /// <param name="builder">The Semantic Kernel builder to configure.</param>
    /// <param name="config">Configuration containing provider settings (LlmProvider, ApiKey, Model, Endpoint).</param>
    /// <returns>The configured builder for method chaining.</returns>
    public static void ConfigureProvider(IKernelBuilder builder, IConfiguration config)
    {
        var provider = config["LlmProvider"] ?? "Ollama";
        Console.WriteLine($"[DIAG-LLM] Provider: {provider}");
        Console.WriteLine($"[DIAG-LLM] Provider:Model: {config[$"{provider}:Model"]}");
        Console.WriteLine($"[DIAG-LLM] Provider:Endpoint: {config[$"{provider}:Endpoint"]}");

        switch (provider)
        {
            case "Groq":
                {
                    // Groq uses OpenAI-compatible API at their custom endpoint
                    var endpoint = config["Groq:Endpoint"] ?? "https://api.groq.com/openai/v1";
                    var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
                    var client = new OpenAIClient(new ApiKeyCredential(config["Groq:ApiKey"] ?? string.Empty), clientOptions);
                    builder.AddOpenAIChatCompletion(
                        modelId: config["Groq:Model"] ?? "llama-3.3-70b-versatile",
                        openAIClient: client);
                }
                break;

            case "Grok":
                {
                    // Grok (xAI) uses OpenAI-compatible API at their custom endpoint
                    var endpoint = config["Grok:Endpoint"] ?? "https://api.x.ai/v1";
                    var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
                    var client = new OpenAIClient(new ApiKeyCredential(config["Grok:ApiKey"] ?? string.Empty), clientOptions);
                    builder.AddOpenAIChatCompletion(
                        modelId: config["Grok:Model"] ?? "grok-beta",
                        openAIClient: client);
                }
                break;

            case "OpenCodeZen":
                {
                    // OpenCode Zen uses OpenAI-compatible API at opencode.ai/zen/v1
                    var endpoint = config["OpenCodeZen:Endpoint"] ?? "https://opencode.ai/zen/v1";
                    var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
                    var client = new OpenAIClient(new ApiKeyCredential(config["OpenCodeZen:ApiKey"] ?? string.Empty), clientOptions);
                    builder.AddOpenAIChatCompletion(
                        modelId: config["OpenCodeZen:Model"] ?? "deepseek-v4-flash-free",
                        openAIClient: client);
                }
                break;

            case "Gemini":
                {
                    // Google AI Studio (Gemini) - uses native Semantic Kernel connector
                    var apiKey = config["Gemini:ApiKey"] ?? string.Empty;
                    var model = config["Gemini:Model"] ?? "gemini-3.6-flash";
                    builder.AddGoogleAIGeminiChatCompletion(
                        modelId: model,
                        apiKey: apiKey);
                }
                break;

            case "OpenAI":
                {
                    var endpoint = config["OpenAI:Endpoint"] ?? "http://localhost:9931/v1";
                    if (endpoint.EndsWith("/")) endpoint = endpoint[..^1];

                    var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
                    var client = new OpenAIClient(new ApiKeyCredential(config["OpenAI:ApiKey"] ?? "llama-cpp"), clientOptions);
                    builder.AddOpenAIChatCompletion(
                        modelId: config["OpenAI:Model"] ?? "local-model",
                        openAIClient: client);

                    // NOTE: Embedding service not configured here. It is registered separately
                    //       in KernelExtensions via the OpenAI-compatible embedding transport.
                }
                break;

            default:
                {
                    // Default to Ollama (local LLM server)
                    var endpoint = config["Ollama:Endpoint"] ?? "http://localhost:11434/v1";
                    if (endpoint.EndsWith("/")) endpoint = endpoint[..^1];

                    var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
                    var client = new OpenAIClient(new ApiKeyCredential("ollama"), clientOptions);
                    builder.AddOpenAIChatCompletion(
                        modelId: config["Ollama:Model"] ?? "llama3.1",
                        openAIClient: client);

                    // NOTE: Embedding service not configured here. It is registered separately
                    //       in KernelExtensions via the OpenAI-compatible embedding transport.
                }
                break;
        }
    }
}
