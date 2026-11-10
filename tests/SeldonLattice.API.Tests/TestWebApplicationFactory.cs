using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.API.Tests;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "SeldonLatticeTest", Guid.NewGuid().ToString("N"));

    // Isolate each host's on-disk store so parallel test classes don't
    // race on the shared default ./data store.dat.

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Point the LLM at a dead port so ascend flows fail fast (connection
        // refused) instead of hanging against a real local inference server.
        // Endpoints assert on authorization outcomes (401/403), not LLM output.
        builder.UseSetting("DataDir", _dataDir);
        builder.UseSetting("LlmProvider", "OpenAI");
        builder.UseSetting("OpenAI:Endpoint", "http://127.0.0.1:1/v1");
        builder.UseSetting("OpenAI:ApiKey", "test-key");
        builder.UseSetting("OpenAI:Model", "test-model");
        builder.UseSetting("HttpClient:LlmTimeoutMinutes", "1");
        // Rate limiting (decision D5) is active inside tests too, and factories are
        // shared per test class (IClassFixture) — lift the ceiling so a busy class
        // never trips a 429 mid-suite. The limiter's own behaviour is config-driven
        // and not what these endpoint tests assert.
        builder.UseSetting("RateLimiting:RequestsPerMinute", "1000000");
        builder.UseSetting("RateLimiting:BurstLimit", "1000000");


        builder.ConfigureServices(services =>
        {
            // Stub the chat service used by health-check flows.
            services.AddSingleton<IChatService>(new StubChatService());
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { Directory.Delete(_dataDir, true); } catch { }
        }
        base.Dispose(disposing);
    }
}
