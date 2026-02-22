using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataverseForge.Application.Tests.Agents;

public class DataPopulationAgentTests
{
    private readonly Mock<IChatClient> _mockChatClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<IConversationStore> _mockConversationStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();
    private readonly Mock<ILogger<DataPopulationAgent>> _mockLogger = new();
    private readonly List<AITool> _tools = [];
    private readonly IReadOnlyList<string> _handoffTargets = [AgentNames.SolutionArchitect, AgentNames.Tester];

    private DataPopulationAgent CreateAgent() =>
        new(_mockChatClient.Object, _mockProjectStateStore.Object,
            _mockConversationStore.Object, _mockSessionContext.Object,
            _tools, _handoffTargets, _mockLogger.Object);

    private void SetupDefaults(string sessionId, string responseText = "I'll populate the data now.")
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
    public void Name_ReturnsDataPopulation()
    {
        var agent = CreateAgent();
        Assert.Equal(AgentNames.DataPopulation, agent.Name);
    }

    [Fact]
    public void HandoffTargets_ContainsExpectedAgents()
    {
        var agent = CreateAgent();
        Assert.Equal(2, agent.HandoffTargets.Count);
        Assert.Contains(AgentNames.SolutionArchitect, agent.HandoffTargets);
        Assert.Contains(AgentNames.Tester, agent.HandoffTargets);
    }

    [Fact]
    public async Task ChatAsync_UsesDataPopulationSystemPrompt()
    {
        var sessionId = "dp-test-1";
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
        await agent.ChatAsync(sessionId, "Populate reference data", CancellationToken.None);

        Assert.NotNull(capturedMessages);
        var messages = capturedMessages!.ToList();
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Contains("Data Population", messages[0].Text);
    }

    [Fact]
    public async Task ChatAsync_SetsSessionContext()
    {
        var sessionId = "dp-test-2";
        SetupDefaults(sessionId);

        var agent = CreateAgent();
        await agent.ChatAsync(sessionId, "Hello", CancellationToken.None);

        _mockSessionContext.VerifySet(sc => sc.SessionId = sessionId, Times.Once());
    }

    [Fact]
    public async Task ChatAsync_SavesProjectStateAfterTurn()
    {
        var sessionId = "dp-test-3";
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
        var sessionId = "dp-test-4";
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
