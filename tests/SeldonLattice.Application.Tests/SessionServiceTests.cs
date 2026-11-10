using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SeldonLattice.Domain.Interfaces;
using SeldonLattice.Domain.Models.Storage;
using SeldonLattice.Infrastructure.Sessions;
using Xunit;

namespace SeldonLattice.Application.Tests;

public class SessionServiceTests
{
    private const string Owner1 = "user-1";
    private const string Owner2 = "user-2";

    private readonly IServiceProvider _serviceProvider;

    public SessionServiceTests()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "seldon-test", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dataDir);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataDir"] = dataDir,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton(configuration);
        services.AddDbContextFactory<SessionsDbContext>(options =>
            options.UseSqlite($"Data Source={Path.Combine(dataDir, "sessions.db")}"));
        services.AddScoped<ISessionService, SessionService>();

        _serviceProvider = services.BuildServiceProvider();
        var db = _serviceProvider.GetRequiredService<IDbContextFactory<SessionsDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
        db.Dispose();
    }

    private ISessionService Service => _serviceProvider.GetRequiredService<ISessionService>();

    private static async Task<string> CreateSessionAsync(ISessionService service, string ownerId, string rootQuestion, string framework)
    {
        return await service.CreateSessionAsync(rootQuestion, framework, ownerId);
    }

    [Fact]
    public async Task CreateSessionAsync_ReturnsNonEmptySessionId()
    {
        var sessionId = await CreateSessionAsync(Service, Owner1, "What is philosophy?", "philosophy");

        Assert.False(string.IsNullOrEmpty(sessionId));
    }

    [Fact]
    public async Task GetUserSessionAsync_ReturnsSessionAfterCreation()
    {
        var sessionId = await CreateSessionAsync(Service, Owner1, "What is philosophy?", "philosophy");

        var session = await Service.GetUserSessionAsync(sessionId, Owner1);

        Assert.NotNull(session);
        Assert.Equal(sessionId, session.SessionId);
        Assert.Equal("What is philosophy?", session.RootQuestion);
        Assert.Equal("philosophy", session.Framework);
        Assert.Equal(SessionStatus.Active, session.Status);
        Assert.Equal(0, session.NodeCount);
        Assert.Equal(0, session.MaxLevel);
    }

    [Fact]
    public async Task GetSessionAsync_ReturnsNullForNonExistentSession()
    {
        var session = await Service.GetSessionAsync("non-existent-id");

        Assert.Null(session);
    }

    [Fact]
    public async Task GetUserSessionAsync_ReturnsNullForOtherOwner()
    {
        var sessionId = await CreateSessionAsync(Service, Owner1, "What is philosophy?", "philosophy");

        var session = await Service.GetUserSessionAsync(sessionId, Owner2);

        Assert.Null(session);
    }

    [Fact]
    public async Task ListSessionsAsync_ReturnsEmptyListInitially()
    {
        var sessions = await Service.ListSessionsAsync(Owner1);

        Assert.Empty(sessions);
    }

    [Fact]
    public async Task ListSessionsAsync_ReturnsOnlySessionsOwnedByUser()
    {
        await CreateSessionAsync(Service, Owner1, "First question", "philosophy");
        await CreateSessionAsync(Service, Owner1, "Second question", "research");
        await CreateSessionAsync(Service, Owner2, "Someone else's question", "philosophy");

        var owner1Sessions = (await Service.ListSessionsAsync(Owner1)).ToList();
        var owner2Sessions = (await Service.ListSessionsAsync(Owner2)).ToList();

        Assert.Equal(2, owner1Sessions.Count);
        Assert.Single(owner2Sessions);
        Assert.All(owner1Sessions, s => Assert.Equal(Owner1, s.OwnerId));
    }

    [Fact]
    public async Task ListSessionsAsync_ReturnsMultipleSessionsOrderedByCreationDescending()
    {
        await CreateSessionAsync(Service, Owner1, "First question", "philosophy");
        await CreateSessionAsync(Service, Owner1, "Second question", "philosophy");

        var sessions = (await Service.ListSessionsAsync(Owner1)).ToList();

        Assert.Equal(2, sessions.Count);
        Assert.Equal("Second question", sessions[0].RootQuestion);
    }

    [Fact]
    public async Task ListSessionsAsync_FiltersByFramework()
    {
        await CreateSessionAsync(Service, Owner1, "Classical question", "philosophy");
        await CreateSessionAsync(Service, Owner1, "Layered question", "research");

        var classicalSessions = (await Service.ListSessionsAsync(Owner1, framework: "philosophy")).ToList();

        Assert.Single(classicalSessions);
        Assert.Equal("philosophy", classicalSessions[0].Framework);
    }

    [Fact]
    public async Task SetSessionStatusAsync_ArchivesSession_AndExcludesItFromActiveList()
    {
        var sessionId = await CreateSessionAsync(Service, Owner1, "What is philosophy?", "philosophy");

        await Service.SetSessionStatusAsync(sessionId, SessionStatus.Archived);

        var activeSessions = await Service.ListSessionsAsync(Owner1);
        var archivedSessions = await Service.ListSessionsAsync(Owner1, status: SessionStatus.Archived);

        Assert.Empty(activeSessions);
        Assert.Single(archivedSessions);
        Assert.Equal(sessionId, archivedSessions.First().SessionId);
        Assert.Equal(SessionStatus.Archived, archivedSessions.First().Status);
    }

    [Fact]
    public async Task UpdateSessionAsync_UpdatesMetadata()
    {
        var sessionId = await CreateSessionAsync(Service, Owner1, "What is philosophy?", "philosophy");

        await Service.UpdateSessionAsync(sessionId, nodeCount: 9, maxLevel: 2);

        var session = await Service.GetUserSessionAsync(sessionId, Owner1);
        Assert.NotNull(session);
        Assert.Equal(9, session.NodeCount);
        Assert.Equal(2, session.MaxLevel);
    }
}