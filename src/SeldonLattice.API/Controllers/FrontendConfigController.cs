using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace SeldonLattice.API.Controllers;

/// <summary>
/// Serves the FrontendConfig section of appsettings.json to the frontend,
/// eliminating hard-coded behavioural constants in the client.
/// </summary>
[ApiController]
[Route("api/frontend-config")]
[DisableRateLimiting] // SPA boot fetch happens before any key exists (decision D5 exemptions)
public class FrontendConfigController(IConfiguration config) : ControllerBase
{
    [HttpGet]
    public IActionResult GetFrontendConfig()
    {
        var section = config.GetSection("FrontendConfig");
        if (!section.Exists())
        {
            return NotFound(new { error = "FrontendConfig section not found in configuration." });
        }

        var values = new Dictionary<string, object?>();
        foreach (var child in section.GetChildren())
        {
            var key = JsonNamingPolicy.CamelCase.ConvertName(child.Key);
            var nested = child.GetChildren().ToList();

            if (nested.Count > 0)
            {
                values[key] = nested
                    .OrderBy(c => int.TryParse(c.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                        ? index
                        : int.MaxValue)
                    .Select(c => CoerceScalar(c.Value))
                    .ToList();
            }
            else if (child.Value is not null)
            {
                values[key] = CoerceScalar(child.Value);
            }
        }

        return Ok(values);
    }

    /// <summary>
    /// Configuration values are always strings. Emit numbers as numbers so the response is
    /// typed; anything that does not parse as a number stays a string (e.g. the version).
    /// </summary>
    private static object? CoerceScalar(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : value;
    }
}
