using DataverseForge.Infrastructure.Stores;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataverseForge.Infrastructure.Tests.Stores;

public class SqliteConversationStoreTests : IDisposable
{
    private readonly SqliteProjectStateStore _stateStore;
    private readonly SqliteConversationStore _conversationStore;

    public SqliteConversationStoreTests()
    {
        var dbName = $"file:test_{Guid.NewGuid():N}?mode=memory&cache=shared";
        var connectionString = $"Data Source={dbName}";
        _stateStore = new SqliteProjectStateStore(connectionString, NullLogger<SqliteProjectStateStore>.Instance);
        _conversationStore = new SqliteConversationStore(connectionString, _stateStore, NullLogger<SqliteConversationStore>.Instance);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetOrCreateAsync_ReturnsEmptyListForNewSession()
    {
        // Must create session first (conversation shares the sessions table)
        await _stateStore.GetOrCreateAsync("conv-empty");

        var messages = await _conversationStore.GetOrCreateAsync("conv-empty");

        Assert.Empty(messages);
    }

    [Fact]
    public async Task SaveAsync_ThenLoad_RoundTrips()
    {
        await _stateStore.GetOrCreateAsync("conv-rt");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello, help me build a Dataverse solution"),
            new(ChatRole.Assistant, "I'd be happy to help! Let me start by checking your environment.")
        };

        await _conversationStore.SaveAsync("conv-rt", messages);

        var loaded = await _conversationStore.GetOrCreateAsync("conv-rt");

        Assert.Equal(2, loaded.Count);
        Assert.Equal("user", loaded[0].Role.Value);
        Assert.Equal("Hello, help me build a Dataverse solution", loaded[0].Text);
        Assert.Equal("assistant", loaded[1].Role.Value);
        Assert.Contains("I'd be happy to help", loaded[1].Text);
    }

    [Fact]
    public async Task SaveAsync_PreservesContent()
    {
        await _stateStore.GetOrCreateAsync("conv-content");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a Solution Architect agent."),
            new(ChatRole.User, "Create a table called test_feedback"),
            new(ChatRole.Assistant, "I'll create the feedback table for you.")
        };

        await _conversationStore.SaveAsync("conv-content", messages);
        var loaded = await _conversationStore.GetOrCreateAsync("conv-content");

        Assert.Equal(3, loaded.Count);
        Assert.Equal("system", loaded[0].Role.Value);
        Assert.Equal("You are a Solution Architect agent.", loaded[0].Text);
    }

    [Fact]
    public async Task MultipleSessions_AreIsolated()
    {
        await _stateStore.GetOrCreateAsync("conv-a");
        await _stateStore.GetOrCreateAsync("conv-b");

        var messagesA = new List<ChatMessage>
        {
            new(ChatRole.User, "Session A message")
        };
        var messagesB = new List<ChatMessage>
        {
            new(ChatRole.User, "Session B message 1"),
            new(ChatRole.User, "Session B message 2")
        };

        await _conversationStore.SaveAsync("conv-a", messagesA);
        await _conversationStore.SaveAsync("conv-b", messagesB);

        var loadedA = await _conversationStore.GetOrCreateAsync("conv-a");
        var loadedB = await _conversationStore.GetOrCreateAsync("conv-b");

        Assert.Single(loadedA);
        Assert.Equal("Session A message", loadedA[0].Text);
        Assert.Equal(2, loadedB.Count);
        Assert.Equal("Session B message 1", loadedB[0].Text);
    }
}
