using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SeldonLattice.API.Models;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models;
using SeldonLattice.Domain.Options;

namespace SeldonLattice.API.Controllers;

/// <summary>
/// Endpoints for generating sigil patterns from content-addressed identifiers (CIDs).
/// </summary>
[ApiController]
[Route("api/sigil")]
public class SigilController(
    ISigilGenerator generator,
    IOptions<SigilOptions> options,
    ILogger<SigilController> logger)
    : ControllerBase
{
    private readonly SigilOptions _options = options.Value;

    /// <summary>
    /// Generates a sigil pattern from a content-addressed identifier.
    /// </summary>
    /// <param name="cid">The content identifier (CID) to generate a sigil for.</param>
    /// <param name="algorithm">Optional algorithm selector. Default: all (returns all algorithms).</param>
    /// <param name="size">Optional output dimension in pixels (square). Default: 256. Max: configurable.</param>
    /// <param name="format">Output format: "svg" (default) or "json".</param>
    /// <returns>SVG markup or JSON data of the generated sigil(s).</returns>
    [HttpGet("{cid}")]
    [Authorize(Policy = "ReaderOnly")]
    public async Task<IResult> GenerateSigil(
        [FromRoute] string cid,
        [FromQuery] string? algorithm,
        [FromQuery] int? size,
        [FromQuery] string? format,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cid))
        {
            logger.LogWarning("Sigil request with empty CID");
            return Results.BadRequest(new { error = "CID is required" });
        }

        // Validate CID format (basic check: should contain a hash)
        var cidParts = cid.Split('-', 2);
        if (cidParts.Length == 2 && string.IsNullOrEmpty(cidParts[1]))
        {
            logger.LogWarning("Invalid CID format: {Cid}", cid);
            return Results.BadRequest(new { error = "Invalid CID format" });
        }

        var effectiveSize = size ?? _options.DefaultSize;
        if (effectiveSize < 1) effectiveSize = _options.DefaultSize;
        if (effectiveSize > _options.MaxSize) effectiveSize = _options.MaxSize;

        var isJson = format?.Equals("json", StringComparison.OrdinalIgnoreCase) == true;

        // Determine which algorithms to generate
        if (algorithm is null or "all" or "")
        {
            // Generate all algorithms
            var results = await generator.GenerateAllAsync(cid, effectiveSize);
            var dtos = results.Select(r => new Sigil
            {
                Algorithm = r.Algorithm.ToString().ToLowerInvariant(),
                Cid = r.Cid,
                Svg = r.Svg,
                Size = r.Size
            }).ToArray();

            if (isJson)
                return Results.Ok(new { cid, size = effectiveSize, sigils = dtos });

            // Combine all into one SVG
            var combined = BuildCombinedSvg(dtos, effectiveSize);
            return Results.Content(combined, "image/svg+xml");
        }

        // Single algorithm requested
        if (!TryParseAlgorithm(algorithm, out var algo))
        {
            logger.LogWarning("Unknown sigil algorithm requested: {Algorithm}", algorithm);
            return Results.BadRequest(new { error = $"Unknown algorithm '{algorithm}'. Valid options: lissajous, mandala, fractal_tree, all" });
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var result = await generator.GenerateAsync(cid, algo, effectiveSize);
            var dto = new Sigil
            {
                Algorithm = result.Algorithm.ToString().ToLowerInvariant(),
                Cid = result.Cid,
                Svg = result.Svg,
                Size = result.Size
            };

            if (isJson)
                return Results.Ok(new { cid, size = effectiveSize, algorithm = dto.Algorithm, sigil = dto });

            return Results.Content(dto.Svg, "image/svg+xml");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Results.Problem("Sigil generation was cancelled.", statusCode: 499);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate sigil for CID {Cid} with algorithm {Algorithm}", cid, algorithm);
            return Results.Problem("Failed to generate sigil.", statusCode: 500);
        }
    }

    private static bool TryParseAlgorithm(string input, out SigilAlgorithm algorithm)
    {
        // Accept user-friendly spellings: "vector_walk", "vector-walk" and
        // "VectorWalk" all resolve to the same member - separators are stripped,
        // not rewritten to underscores (deferred fix 2, handoff_2026-10-20).
        var normalized = input.Trim().ToLowerInvariant()
            .Replace("_", string.Empty)
            .Replace("-", string.Empty);
        return Enum.TryParse(normalized, ignoreCase: true, out algorithm);
    }

    private static string BuildCombinedSvg(Sigil[] sigils, int cellSize)
    {
        var cols = (int)Math.Ceiling(Math.Sqrt(sigils.Length));
        var rows = (int)Math.Ceiling((double)sigils.Length / cols);
        var totalWidth = cols * cellSize;
        var totalHeight = rows * cellSize;
        var sb = new System.Text.StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{totalWidth}\" height=\"{totalHeight}\" viewBox=\"0 0 {totalWidth} {totalHeight}\">");

        for (int i = 0; i < sigils.Length; i++)
        {
            var col = i % cols;
            var row = i / cols;
            var x = col * cellSize;
            var y = row * cellSize;
            sb.Append($"<g transform=\"translate({x},{y})\">");
            sb.Append(sigils[i].Svg);
            sb.Append($"<text x=\"4\" y=\"{cellSize - 4}\" font-size=\"9\" fill=\"#64748b\">{sigils[i].Algorithm}</text>");
            sb.Append("</g>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }
}
