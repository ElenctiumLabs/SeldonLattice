using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Infrastructure.Ingestion;

namespace SeldonLattice.API.Extensions;

/// <summary>
/// Ingestion &amp; falsification module: the external data connectors (Europeana, Yahoo
/// connectors), the document normaliser and audit engine, and the orchestrator that ties them
/// together.
/// </summary>
public static class IngestionExtensions
{
    public static void AddSeldonLatticeIngestion(this IServiceCollection services,
        IConfiguration configuration)
    {
        // Add Ingestion & Falsification Module
        var ingestionTimeout = configuration.GetValue("HttpClient:IngestionTimeoutSeconds", 30);
        var europeanaApiKey = configuration["Ingestion:Europeana:ApiKey"] ?? "";
        if (!string.IsNullOrEmpty(europeanaApiKey))
        {
            services.AddHttpClient<EuropeanaConnector>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(ingestionTimeout);
            });
            services.AddSingleton<IDataConnector>(sp =>
                sp.GetRequiredService<EuropeanaConnector>());
        }

        services.AddSingleton<DocumentNormalizer>();
        services.AddSingleton<AuditEngine>();
        services.AddSingleton<IIngestionOrchestrator, IngestionOrchestrator>();
    }
}
