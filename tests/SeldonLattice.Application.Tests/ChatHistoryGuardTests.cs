using Microsoft.SemanticKernel.ChatCompletion;
using SeldonLattice.Application.Extensions;
using Xunit;

namespace SeldonLattice.Application.Tests;

public class ChatHistoryGuardTests
{
    [Fact]
    public void SystemOnlyHistory_IsConvertedToSingleUserMessage()
    {
        var history = new ChatHistory();
        history.AddSystemMessage("You are the moderator. Synthesize the debate.");

        var fixedHistory = history.EnsureAtLeastOneUserMessage();

        Assert.Single(fixedHistory);
        Assert.Equal(AuthorRole.User, fixedHistory[0].Role);
        Assert.Equal("You are the moderator. Synthesize the debate.", fixedHistory[0].Content);
    }

    [Fact]
    public void SystemOnlyHistory_WithOutputContract_KeepsBothMessagesInOrder()
    {
        var history = new ChatHistory();
        history.AddSystemMessage("contract first");
        history.AddSystemMessage("role second");

        var fixedHistory = history.EnsureAtLeastOneUserMessage();

        Assert.Equal(2, fixedHistory.Count);
        Assert.Equal(AuthorRole.User, fixedHistory[0].Role);
        Assert.Equal(AuthorRole.System, fixedHistory[1].Role);
        Assert.Equal("contract first", fixedHistory[0].Content);
        Assert.Equal("role second", fixedHistory[1].Content);
    }

    [Fact]
    public void MixedRoleHistory_IsReturnedUnchanged()
    {
        var history = new ChatHistory();
        history.AddSystemMessage("role");
        history.AddUserMessage("the debate");

        var fixedHistory = history.EnsureAtLeastOneUserMessage();

        Assert.Same(history, fixedHistory);
        Assert.Equal(2, fixedHistory.Count);
        Assert.Equal(AuthorRole.System, fixedHistory[0].Role);
        Assert.Equal(AuthorRole.User, fixedHistory[1].Role);
    }

    [Fact]
    public void UserOnlyHistory_IsReturnedUnchanged()
    {
        var history = new ChatHistory();
        history.AddUserMessage("the debate");

        Assert.Same(history, history.EnsureAtLeastOneUserMessage());
    }
}