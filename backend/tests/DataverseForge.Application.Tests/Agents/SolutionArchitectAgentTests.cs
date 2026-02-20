using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataverseForge.Application.Tests.Agents;

public class SolutionArchitectAgentTests
{
    private readonly Mock<IChatClient> _mockChatClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<IConversationStore> _mockConversationStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();
    private readonly Mock<ILogger<SolutionArchitectAgent>> _mockLogger = new();
    private readonly List<AITool> _tools = [];
    private readonly IReadOnlyList<string> _handoffTargets = [AgentNames.BusinessAnalyst];

    private SolutionArchitectAgent CreateAgent() =>
        new(_mockChatClient.Object, _mockProjectStateStore.Object,
            _mockConversationStore.Object, _mockSessionContext.Object,
            _tools, _handoffTargets, _mockLogger.Object);

    private void SetupDefaults(string sessionId, string responseText = "Hello! Let me help you.")
    {
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectState { ProjectId = sessionId, CreatedAt = DateTime.UtcNow });

        _mockConversationStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var chatResponse = new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, responseText)]);

        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);
    }

    [Fact]
    public async Task ChatAsync_SystemPromptIsFirstMessageInHistory()
    {
        // Arrange
        var sessionId = "test-session-1";
        SetupDefaults(sessionId);

        IEnumerable<ChatMessage>? capturedMessages = null;
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (msgs, _, _) => capturedMessages = msgs.ToList())
            .ReturnsAsync(new ChatResponse(
                [new ChatMessage(ChatRole.Assistant, "Hi")]));

        var agent = CreateAgent();

        // Act
        await agent.ChatAsync(sessionId, "Hello", CancellationToken.None);

        // Assert
        Assert.NotNull(capturedMessages);
        var messages = capturedMessages!.ToList();
        Assert.True(messages.Count >= 2);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Contains("Solution Architect", messages[0].Text);
    }

    [Fact]
    public async Task ChatAsync_ToolsPassedInChatOptions()
    {
        // Arrange
        var sessionId = "test-session-2";
        SetupDefaults(sessionId);

        ChatOptions? capturedOptions = null;
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(new ChatResponse(
                [new ChatMessage(ChatRole.Assistant, "Hi")]));

        var agent = CreateAgent();

        // Act
        await agent.ChatAsync(sessionId, "Hello", CancellationToken.None);

        // Assert
        Assert.NotNull(capturedOptions);
        Assert.NotNull(capturedOptions!.Tools);
    }

    [Fact]
    public async Task ChatAsync_ProjectStateSavedAfterEachTurn()
    {
        // Arrange
        var sessionId = "test-session-3";
        SetupDefaults(sessionId);
        var agent = CreateAgent();

        // Act
        await agent.ChatAsync(sessionId, "Hello", CancellationToken.None);

        // Assert
        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ChatAsync_SessionContinuityAcrossCalls()
    {
        // Arrange
        var sessionId = "test-session-4";
        var sharedHistory = new List<ChatMessage>();

        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectState { ProjectId = sessionId, CreatedAt = DateTime.UtcNow });

        _mockConversationStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sharedHistory);

        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                [new ChatMessage(ChatRole.Assistant, "Response")]));

        var agent = CreateAgent();

        // Act — two turns on the same session
        await agent.ChatAsync(sessionId, "First message", CancellationToken.None);
        await agent.ChatAsync(sessionId, "Second message", CancellationToken.None);

        // Assert — history should contain system + user + assistant from both turns
        // System(1) + User(1) + Assistant(1) + User(2) + Assistant(2) = 5
        // But system gets replaced in-place, so still 1 system message
        Assert.Contains(sharedHistory, m => m.Role == ChatRole.System);
        var userMessages = sharedHistory.Where(m => m.Role == ChatRole.User).ToList();
        Assert.Equal(2, userMessages.Count);
        Assert.Contains("First message", userMessages[0].Text);
        Assert.Contains("Second message", userMessages[1].Text);
    }

    [Fact]
    public async Task ChatAsync_ReturnsResponseText()
    {
        // Arrange
        var sessionId = "test-session-5";
        var expectedText = "I'm the Solution Architect. Let's get started!";
        SetupDefaults(sessionId, expectedText);
        var agent = CreateAgent();

        // Act
        var result = await agent.ChatAsync(sessionId, "Hello", CancellationToken.None);

        // Assert
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(expectedText, result.Response);
        Assert.NotNull(result.ProjectState);
    }

    [Fact]
    public async Task GetProjectStateAsync_ReturnsStateFromStore()
    {
        // Arrange
        var sessionId = "test-session-6";
        var expectedState = new ProjectState { ProjectId = sessionId, ProjectName = "Test" };
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedState);

        var agent = CreateAgent();

        // Act
        var result = await agent.GetProjectStateAsync(sessionId, CancellationToken.None);

        // Assert
        Assert.Equal(expectedState.ProjectId, result.ProjectId);
        Assert.Equal("Test", result.ProjectName);
    }
}
