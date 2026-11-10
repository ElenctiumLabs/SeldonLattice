using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using SeldonLattice.Domain.Models;
using SeldonLattice.Domain.Models.Storage;

namespace SeldonLattice.Infrastructure.Ingestion;

/// <summary>
/// Transforms raw source documents into normalized IngestedDocuments,
/// generating embeddings for semantic comparison with the philosophy engine's debate nodes.
/// </summary>
public class DocumentNormalizer(Kernel kernel, ILogger<DocumentNormalizer> logger)
{
    [Experimental("SKEXP0001")]
    public async Task<IngestedDocument> NormalizeAsync(RawSourceDocument raw, CancellationToken ct)
    {
        var body = SanitizeBody(raw.Body);
        var title = SanitizeTitle(raw.Title);
        var combinedText = $"{title}. {body}";
        if (string.IsNullOrWhiteSpace(combinedText))
            combinedText = "No content available";

        float[] embedding;
        try
        {
            var embeddingService = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
            var result = await embeddingService.GenerateAsync([combinedText[..Math.Min(combinedText.Length, 8192)]], cancellationToken: ct);
            embedding = result[0].Vector.ToArray();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Embedding generation failed for document {ExternalId}, using empty embedding", raw.ExternalId);
            embedding = [];
        }

        return new IngestedDocument
        {
            ExternalId = raw.ExternalId,
            SourceId = raw.Metadata.GetValueOrDefault("source", "unknown"),
            Title = title,
            Body = body,
            Culture = raw.Culture,
            Domain = raw.Domain,
            SourceTimestamp = raw.SourceTimestamp,
            IngestedAt = DateTime.UtcNow,
            Embedding = embedding,
            Metadata = raw.Metadata
        };
    }

    private static string SanitizeBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        // Strip HTML tags that might come from RSS feeds
        var cleaned = System.Text.RegularExpressions.Regex.Replace(body, "<[^>]*>", " ");
        // Collapse whitespace
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned.Length > 10000 ? cleaned[..10000] : cleaned;
    }

    private static string SanitizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Untitled";
        return title.Length > 500 ? title[..500] : title;
    }
}