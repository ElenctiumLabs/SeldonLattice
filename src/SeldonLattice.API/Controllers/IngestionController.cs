using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeldonLattice.API.Authentication;
using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.API.Controllers;

/// <summary>
/// Ingestion and falsification related API endpoints.
/// </summary>
/// <remarks>
/// Granular split: the compute/write routes (cycle, source, falsify) require the
/// <c>ingest</c> role; the read routes (audit, documents, citations) require
/// <c>reader</c>. The citations route keeps its own <c>X-Api-Key</c> belt-and-braces
/// check when <c>ApiKeys:Ingestion</c> is configured.
/// </remarks>
[ApiController]
[Route("api/ingestion")]
public class IngestionController(
    IIngestionOrchestrator ingestion,
    ICitationProvider citations,
    IConfiguration config)
    : ControllerBase
{
    /// <summary>Runs a full ingestion cycle.</summary>
    [HttpPost("cycle")]
    [Authorize(Policy = "IngestOnly")]
    public async Task<IActionResult> RunIngestionCycle(CancellationToken ct)
    {
        var result = await ingestion.RunIngestionCycleAsync(ct);
        return Ok(result);
    }

    /// <summary>Ingests from a specific source with a targeted query.</summary>
    [HttpPost("source")]
    [Authorize(Policy = "IngestOnly")]
    public async Task<IActionResult> IngestFromSource([FromBody] IngestSourceRequest req, CancellationToken ct)
    {
        var result = await ingestion.IngestFromSourceAsync(req.SourceId, req.Query, req.MaxResults, ct);
        return Ok(result);
    }

    /// <summary>Audits the lattice for bias.</summary>
    [HttpGet("audit")]
    [Authorize(Policy = "ReaderOnly")]
    public async Task<IActionResult> AuditBias(CancellationToken ct)
    {
        var result = await ingestion.AuditBiasAsync(ct);
        return Ok(result);
    }

    /// <summary>Runs the falsifier.</summary>
    [HttpPost("falsify")]
    [Authorize(Policy = "IngestOnly")]
    public async Task<IActionResult> RunFalsification(CancellationToken ct)
    {
        var result = await ingestion.RunFalsificationAsync(ct);
        return Ok(result);
    }

    /// <summary>Returns ingested documents, optionally filtered.</summary>
    [HttpGet("documents")]
    [Authorize(Policy = "ReaderOnly")]
    public async Task<IActionResult> GetIngestedDocuments(
        [FromQuery] string? sourceId,
        [FromQuery] string? culture,
        [FromQuery] string? domain,
        CancellationToken ct)
    {
        var previewLen = config.GetValue("Defaults:BodyPreviewLength", 200);
        var docs = await ingestion.GetIngestedDocumentsAsync(sourceId, culture, domain, ct);
        var result = docs.Select(d => new
        {
            cid = d.GetCid().ToString(),
            sourceId = d.SourceId,
            title = d.Title,
            culture = d.Culture,
            domain = d.Domain,
            sourceTimestamp = d.SourceTimestamp,
            ingestedAt = d.IngestedAt,
            bodyPreview = d.Body.Length > previewLen ? d.Body[..previewLen] + "..." : d.Body
        });
        return Ok(result);
    }

    /// <summary>
    /// Citation lookup: returns top-N authority references for a query.
    /// </summary>
    /// <remarks>
    /// Security: when <c>Security:RequireApiKey</c> is true (default), the request must include
    /// header <c>X-Api-Key</c> matching <c>ApiKeys:Ingestion</c> - the check only applies when
    /// that config value is actually set.
    /// </remarks>
    [HttpGet("citations")]
    [Authorize(Policy = "ReaderOnly")]
    public async Task<IActionResult> GetCitations(
        [FromQuery] string? q,
        [FromQuery] int topN = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "Query parameter 'q' is required." });

        var requireKey = config.GetValue("Security:RequireApiKey", true);
        var requiredKey = config["ApiKeys:Ingestion"]; // optional: only enforced when set
        if (requireKey && !string.IsNullOrEmpty(requiredKey))
        {
            // Constant-time compare: never == on secret material.
            if (!Request.Headers.TryGetValue("X-Api-Key", out var provided) ||
                !ApiKeyComparer.FixedTimeEquals(provided.ToString(), requiredKey))
            {
                return Unauthorized();
            }
        }

        var results = await citations.GetTopCitationsAsync(q, topN, ct);
        return Ok(results);
    }
}
