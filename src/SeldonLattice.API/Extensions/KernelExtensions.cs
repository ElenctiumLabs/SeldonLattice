using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Embeddings;
using SeldonLattice.Application.Agents;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Options;
using SeldonLattice.Infrastructure.Audit;
using SeldonLattice.Infrastructure.Services;

namespace SeldonLattice.API.Extensions;

/// <summary>
/// Builds the Semantic Kernel singleton and the chat/embedding services registered on it.
/// Provider selection is driven entirely by configuration: the hand-rolled
/// OpenAI-compatible services for local llama.cpp, and the shared provider factory
/// for Groq, Grok, OpenCodeZen, Gemini and Ollama.
/// </summary>
public static class KernelExtensions
{
    public static void AddSeldonLatticeKernel(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<Kernel>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var kernelBuilder = Kernel.CreateBuilder();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            var provider = config["LlmProvider"];

            // Gate the parse-awareness prompt contracts: "PromptContracts:Enabled" (bool) explicitly
            // overrides; otherwise they are auto-disabled for models known to ignore structured
            // output (e.g. mistral-7b-instruct, which answers the [REASONING]/[RESPONSE] contract
            // with bare prose and would just pay the token cost).
            {
                var overrideSetting = config.GetValue<bool?>("PromptContracts:Enabled");
                var modelName = provider is null
                    ? null
                    : config[$"{provider}:Model"];
                PromptContracts.Enabled = overrideSetting ?? !PromptContracts.IsLikelyProseOnlyModel(modelName);
            }

            // Register the hand-rolled OpenAI-compatible services for providers whose responses need
            // quirk handling for llama.cpp, which speaks the OpenAI dialect with edge
            // cases: an omitted embedding "model" field, a null finish_reason, and reasoning models
            // that emit message.reasoning_content instead of message.content.
            if (provider is "OpenAI" or "OpenAITrained" or "LlamaCpp")
            {
                var endpoint = config[$"{provider}:Endpoint"];
                var chatModel = config[$"{provider}:Model"];
                var embeddingModel = config[$"{provider}:EmbeddingModel"] ?? config[$"{provider}:Model"];
                var apiKey = config[$"{provider}:ApiKey"];
                var llmTimeout = config.GetValue("HttpClient:LlmTimeoutMinutes", 10);
                var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(llmTimeout);

                var llmOptions = new LlmOptions();
                config.GetSection("HttpClient:Llm").Bind(llmOptions);

                // Custom chat completion service, wrapped with RecordingChatService for diagnostics
                if (endpoint != null)
                {
                    if (chatModel != null)
                    {
                        if (apiKey != null)
                        {
                            var innerChatService = new OpenAICompatibleChatCompletionService(
                                httpClient,
                                endpoint,
                                chatModel,
                                apiKey,
                                loggerFactory.CreateLogger<OpenAICompatibleChatCompletionService>(),
                                llmOptions);
                            var chatService = new RecordingChatService(
                                innerChatService,
                                loggerFactory.CreateLogger<RecordingChatService>(),
                                config.GetValue("Diagnostics:LogConversations", false));
                            kernelBuilder.Services.AddSingleton<IChatCompletionService>(chatService);
                        }
                    }
                }

                // Custom embedding service
                var embeddingEndpoint = config[$"{provider}:EmbeddingEndpoint"] ?? endpoint;
                // Embedding validation: shape on every response, continuity once per process against
                // the reference vector for the space the stored corpus lives in.
                var embeddingValidation = new EmbeddingValidationOptions();
                config.GetSection("Embeddings:Validation").Bind(embeddingValidation);

                if (embeddingEndpoint != null)
                {
                    if (embeddingModel != null)
                    {
                        if (apiKey != null)
                        {
                            var embeddingService = new OpenAICompatibleEmbeddingService(
                                httpClient,
                                embeddingEndpoint,
                                embeddingModel,
                                apiKey,
                                loggerFactory.CreateLogger<OpenAICompatibleEmbeddingService>(),
                                embeddingValidation,
                                new FileEmbeddingSentinelStore(
                                    sp.GetRequiredService<IMerkleStore>(),
                                    embeddingValidation,
                                    loggerFactory.CreateLogger<FileEmbeddingSentinelStore>()));
                            kernelBuilder.Services.AddSingleton<ITextEmbeddingGenerationService>(embeddingService);
                            kernelBuilder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embeddingService);

                            // The ascent services resolve this from the kernel to trigger a capture after the first
                            // complete round. Registered as the same instance the embedding service holds.
                            kernelBuilder.Services.AddSingleton<IEmbeddingSentinel>(embeddingService);
                        }
                    }
                }
            }
            else
            {
                // Use default provider factory for other providers (Groq, Grok, Ollama, Gemini)
                Infrastructure.AI.LlmProviderFactory.ConfigureProvider(kernelBuilder, config);

                if (provider is "Groq" or "Grok" or "OpenCodeZen" or "Gemini")
                {
                    // Add OpenAI-compatible embedding service for providers that support it
                    var endpoint = config[$"{provider}:EmbeddingEndpoint"] ?? config[$"{provider}:Endpoint"];
                    var apiKey = config[$"{provider}:EmbeddingApiKey"] ?? config[$"{provider}:ApiKey"];
                    var embeddingModel = config[$"{provider}:EmbeddingModel"] ?? config[$"{provider}:Model"];
                    var llmTimeout = config.GetValue("HttpClient:LlmTimeoutMinutes", 10);
                    var httpClient = new HttpClient();
                    httpClient.Timeout = TimeSpan.FromMinutes(llmTimeout);

                    var embeddingValidation = new EmbeddingValidationOptions();
                    config.GetSection("Embeddings:Validation").Bind(embeddingValidation);

                    if (endpoint != null)
                    {
                        if (embeddingModel != null)
                        {
                            if (apiKey != null)
                            {
                                var embeddingService = new OpenAICompatibleEmbeddingService(
                                    httpClient,
                                    endpoint,
                                    embeddingModel,
                                    apiKey,
                                    loggerFactory.CreateLogger<OpenAICompatibleEmbeddingService>(),
                                    embeddingValidation,
                                    new FileEmbeddingSentinelStore(
                                        sp.GetRequiredService<IMerkleStore>(),
                                        embeddingValidation,
                                        loggerFactory.CreateLogger<FileEmbeddingSentinelStore>()));
                                kernelBuilder.Services.AddSingleton<ITextEmbeddingGenerationService>(embeddingService);
                                kernelBuilder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embeddingService);
                                kernelBuilder.Services.AddSingleton<IEmbeddingSentinel>(embeddingService);
                            }
                        }
                    }
                }
            }

            // Wrap provider IChatCompletionService with MEAI Audit & Quality pipeline
            var raw = sp.GetService<OpenAICompatibleChatCompletionService>();
            if (raw == null) return kernelBuilder.Build();
            var auditClient = AuditMiddlewareExtensions.BuildAuditPipeline(
                raw.AsChatClient(),
                sp,
                usageCollector: sp.GetService<ILlmUsageCollector>());
            kernelBuilder.Services.AddSingleton(auditClient.AsChatCompletionService());

            return kernelBuilder.Build();
        });

        // Expose the kernel's MEAi embedding generator to the outer container so DI-resolved
        // services (e.g. DialogueQualityGates) can use it. Resolved lazily from the Kernel
        // singleton. Returns null for providers where no embedding service is registered (gate degrades).
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
        {
            var kernel = sp.GetRequiredService<Kernel>();
            try
            {
                return kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
            }
            catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
            {
                // Provider without an embedding service registered in the kernel.
                return null!;
            }
        });

        services.AddAuditMiddleware(opts => { opts.EnableQualityGate = true; });
    }
}
