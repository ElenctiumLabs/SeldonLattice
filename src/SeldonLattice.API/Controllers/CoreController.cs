using Microsoft.AspNetCore.Mvc;

namespace SeldonLattice.API.Controllers;

/// <summary>
/// Core API endpoints: root.
/// </summary>
[ApiController]
public class CoreController : ControllerBase
{
    /// <summary>
    /// Root endpoint. Returns a plain-text greeting.
    /// </summary>
    [HttpGet("/")]
    public string GetRoot() => "Welcome to Seldon Lattice API";
}