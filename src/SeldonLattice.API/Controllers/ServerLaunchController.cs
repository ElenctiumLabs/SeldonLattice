using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeldonLattice.API.Services;

namespace SeldonLattice.API.Controllers;

/// <summary>
/// Endpoints for launching and stopping the local llama.cpp servers.
/// The frontend polls these to show server status and trigger start/stop actions.
/// </summary>
/// <remarks>
/// Per-action policies (class-level would AND with them): <c>server-status</c> is
/// <c>reader</c>; the start/stop actions spawn or kill local processes
/// and require <c>admin</c> .
/// </remarks>
[ApiController]
[Route("api/admin")]
public class ServerLaunchController(ServerLaunchService service) : ControllerBase
{
    /// <summary>Checks whether llama.cpp chat and embedding servers are running.</summary>
    [HttpGet("server-status")]
    [Authorize(Policy = "ReaderOnly")]
    public IActionResult GetServerStatus()
    {
        var status = service.GetStatus();
        return Ok(new
        {
            chat = new { status.Chat.Running, status.Chat.Starting, status.Chat.Port },
            embeddings = new { status.Embeddings.Running, status.Embeddings.Starting, status.Embeddings.Port },
            allRunning = status.AllRunning,
        });
    }

    /// <summary>Starts any LLM servers that are not currently running.</summary>
    [HttpPost("launch-servers")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> LaunchServers()
    {
        var result = await service.LaunchAllAsync();
        return Ok(new
        {
            launched = result.Launched,
            starting = result.Starting,
            alreadyRunning = result.AlreadyRunning,
            errors = result.Errors,
        });
    }

    /// <summary>Starts a specific llama.cpp server by key (chat, embeddings).</summary>
    [HttpPost("launch-servers/{key}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> LaunchServer(string key)
    {
        var result = await service.LaunchServerAsync(key);
        return Ok(new
        {
            launched = result.Launched,
            starting = result.Starting,
            alreadyRunning = result.AlreadyRunning,
            errors = result.Errors,
        });
    }

    /// <summary>Stops all LLM servers that were started by this API.</summary>
    [HttpPost("stop-servers")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> StopServers()
    {
        var result = await service.StopAllAsync();
        return Ok(new
        {
            stopped = result.Stopped,
            errors = result.Errors,
        });
    }

    /// <summary>Stops a specific llama.cpp server by key (chat, embeddings).</summary>
    [HttpPost("stop-servers/{key}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> StopServer(string key)
    {
        var result = await service.StopServerAsync(key);
        return Ok(new
        {
            stopped = result.Stopped,
            errors = result.Errors,
        });
    }
}
