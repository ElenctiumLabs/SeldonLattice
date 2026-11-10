using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models;

namespace SeldonLattice.Infrastructure.Ingestion;

/// <summary>
/// Connector for Europeana - the European digital cultural heritage platform.
/// Provides art, literature, photography, and historical documents primarily
/// from the Western cultural frame.
/// </summary>
public class EuropeanaConnector(HttpClient httpClient, ILogger<EuropeanaConnector> logger, string apiKey)
    : IDataConnector
{
    public string SourceId => "europeana";
    public string DisplayName => "Europeana Cultural Heritage";
    public string Culture => "western";
    public string Domain => "art";

    public async Task<IReadOnlyList<RawSourceDocument>> FetchAsync(string query, int maxResults, CancellationToken ct)
    {
        try
        {
            var url = $"https://api.europeana.eu/record/v2/search.json?wskey={apiKey}&query={Uri.EscapeDataString(query)}&rows={maxResults}&profile=full";
            var response = await httpClient.GetFromJsonAsync<EuropeanaResponse>(url, ct);
            
            if (response?.Items == null || response.Items.Count == 0)
            {
                logger.LogInformation("Europeana returned 0 results for query: {Query}", query);
                return [];
            }

            var documents = new List<RawSourceDocument>();
            foreach (var item in response.Items)
            {
                var title = item.Title?.FirstOrDefault() ?? "Untitled";
                var description = item.DcDescription?.FirstOrDefault() ?? "";
                var year = item.Year?.FirstOrDefault() ?? "";
                var dataProvider = item.DataProvider?.FirstOrDefault() ?? "";

                documents.Add(new RawSourceDocument
                {
                    ExternalId = item.Id ?? Guid.NewGuid().ToString(),
                    Title = title,
                    Body = description,
                    Culture = "western",
                    Domain = "art",
                    SourceTimestamp = ParseYear(year),
                    Metadata = new Dictionary<string, string>
                    {
                        ["dataProvider"] = dataProvider,
                        ["year"] = year,
                        ["type"] = item.Type ?? "unknown",
                        ["language"] = item.Language?.FirstOrDefault() ?? "unknown",
                        ["rights"] = item.Rights?.FirstOrDefault() ?? "unknown"
                    }
                });
            }

            logger.LogInformation("Europeana fetched {Count} documents for query: {Query}", documents.Count, query);
            return documents;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Europeana fetch failed for query: {Query}", query);
            return [];
        }
    }

    public async Task<IReadOnlyList<RawSourceDocument>> FetchByCultureAsync(string culture, string domain, int maxResults, CancellationToken ct)
    {
        // Europeana is primarily Western art, but we can query for non-Western cultures
        // by searching for specific cultural keywords
        var query = culture switch
        {
            "eastern" => "chinese OR japanese OR korean OR indian art history",
            "indigenous" => "indigenous OR native OR aboriginal OR tribal art",
            "african" => "african OR egyptian art history",
            "islamic" => "islamic OR arabic OR persian art history",
            _ => $"{culture} {domain} history"
        };
        return await FetchAsync(query, maxResults, ct);
    }

    private static DateTime ParseYear(string year)
    {
        if (string.IsNullOrEmpty(year)) return DateTime.MinValue;
        if (int.TryParse(year[..Math.Min(4, year.Length)], out var y) && y > 0)
            return new DateTime(y, 1, 1);
        return DateTime.MinValue;
    }

    // Europeana API response models
    // ReSharper disable ClassNeverInstantiated.Local
    // ReSharper disable UnusedAutoPropertyAccessor.Local
    private class EuropeanaResponse
    {
        public List<EuropeanaItem>? Items { get; init; }
    }

    private class EuropeanaItem
    {
        public string? Id { get; set; }
        public List<string>? Title { get; set; }
        public List<string>? DcDescription { get; set; }
        public List<string>? Year { get; set; }
        public List<string>? DataProvider { get; set; }
        public string? Type { get; set; }
        public List<string>? Language { get; set; }
        public List<string>? Rights { get; set; }
    }
}