using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.Infrastructure.Audit;

/// <summary>
/// Dependency injection extensions for registering the MEAI Audit and Quality middleware pipeline.
/// </summary>
public static class AuditMiddlewareExtensions
{
    /// <summary>
    /// Registers the MEAI Audit and Quality middleware pipeline in DI.
    /// Bridges the pipeline with Semantic Kernel via the SK–MEAI adapter.
    /// </summary>
    public static IServiceCollection AddAuditMiddleware(
        this IServiceCollection services,
        Action<AuditMiddlewareOptions>? configure = null)
    {
        var opts = new AuditMiddlewareOptions();
        configure?.Invoke(opts);

        if (opts.CircularReasoningThreshold < 0f || opts.CircularReasoningThreshold > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(opts.CircularReasoningThreshold),
                "CircularReasoningThreshold must be in [0.0, 1.0].");
        }

        services.AddSingleton(opts);
        services.AddSingleton<AuditMetrics>();

        // Register the composite IChatClient pipeline
        services.AddSingleton<IChatClient>(sp =>
        {
            var skKernel = sp.GetRequiredService<Kernel>();
            var chatService = skKernel.GetRequiredService<IChatCompletionService>();
            var inner = chatService.AsChatClient();

            return BuildAuditPipeline(inner, sp, opts);
        });

        // Bridge the final IChatClient back to IChatCompletionService for any DI consumers
        services.AddSingleton<IChatCompletionService>(sp =>
            sp.GetRequiredService<IChatClient>().AsChatCompletionService());

        return services;
    }

    /// <summary>
    /// Constructs the MEAI middleware pipeline: Inner Client -> AuditDelegatingChatClient -> QualityGateDelegatingChatClient (if enabled).
    /// </summary>
    public static IChatClient BuildAuditPipeline(
        IChatClient inner,
        IServiceProvider sp,
        AuditMiddlewareOptions? options = null,
        Services.ILlmUsageCollector? usageCollector = null)
    {
        var opts = options ?? sp.GetService<AuditMiddlewareOptions>() ?? new AuditMiddlewareOptions();

        var auditClient = new AuditDelegatingChatClient(
            inner,
            sp.GetRequiredService<IMerkleStore>(),
            sp.GetRequiredService<ILogger<AuditDelegatingChatClient>>(),
            opts,
            sp.GetService<AuditMetrics>(),
            usageCollector);

        if (opts.EnableQualityGate)
        {
            return new QualityGateDelegatingChatClient(
                auditClient,
                sp.GetRequiredService<ILogger<QualityGateDelegatingChatClient>>(),
                opts,
                qualityGates: null,
                scopeFactory: sp.GetService<IServiceScopeFactory>());
        }

        return auditClient;
    }
}
