using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataverseForge.Application.Tests.Agents;

public class BusinessAnalystAgentTests
{
    private readonly Mock<IChatClient> _mockChatClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<IConversationStore> _mockConversationStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();
    private readonly Mock<ILogger<BusinessAnalystAgent>> _mockLogger = new();
    private readonly List<AITool> _tools = [];
    private readonly IReadOnlyList<string> _handoffTargets = [AgentNames.SolutionArchitect];

    private BusinessAnalystAgent CreateAgent() =>
        new(_mockChatClient.Object, _mockProjectStateStore.Object,
            _mockConversationStore.Object, _mockSessionContext.Object,
            _tools, _handoffTargets, _mockLogger.Object);

    private void SetupDefaults(string sessionId, string responseText = "Let me gather requirements.")
    {
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectState { ProjectId = sessionId, CreatedAt = DateTime.UtcNow });

        _mockConversationStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(
                [new ChatMessage(ChatRole.Assistant, responseText)]));
    }

    [Fact]
    public void Name_ReturnsBusinessAnalyst()
    {
        var agent = CreateAgent();
        Assert.Equal(AgentNames.BusinessAnalyst, agent.Name);
    }

    [Fact]
    public void HandoffTargets_ContainsOnlySolutionArchitect()
    {
        var agent = CreateAgent();
        Assert.Single(agent.HandoffTargets);
        Assert.Equal(AgentNames.SolutionArchitect, agent.HandoffTargets[0]);
    }

    [Fact]
    public async Task ChatAsync_UsesBusinessAnalystSystemPrompt()
    {
        var sessionId = "ba-test-1";
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

        await agent.ChatAsync(sessionId, "Hello", CancellationToken.None);

        Assert.NotNull(capturedMessages);
        var messages = capturedMessages!.ToList();
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Contains("Business Analyst", messages[0].Text);
        Assert.DoesNotContain("Solution Architect for Dataverse", messages[0].Text);
    }

    [Fact]
    public async Task ChatAsync_SetsSessionContextBeforeCalling()
    {
        var sessionId = "ba-test-2";
        SetupDefaults(sessionId);

        var agent = CreateAgent();
        await agent.ChatAsync(sessionId, "Hello", CancellationToken.None);

        _mockSessionContext.VerifySet(sc => sc.SessionId = sessionId, Times.Once());
    }

    [Fact]
    public async Task ChatAsync_ProjectStateSavedAfterEachTurn()
    {
        var sessionId = "ba-test-3";
        SetupDefaults(sessionId);

        var agent = CreateAgent();
        await agent.ChatAsync(sessionId, "Hello", CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ChatAsync_ToolsPassedInChatOptions()
    {
        var sessionId = "ba-test-4";
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
        await agent.ChatAsync(sessionId, "Hello", CancellationToken.None);

        Assert.NotNull(capturedOptions);
        Assert.NotNull(capturedOptions!.Tools);
    }
}
