using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class HandoffToTesterToolTests
{
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    [Fact]
    public async Task HandoffAsync_SetsActiveAgentToTester()
    {
        var sessionId = "handoff-tester-1";
        var state = new ProjectState { ProjectId = sessionId, ActiveAgentName = AgentNames.SolutionArchitect };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new HandoffToTesterTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        await tool.HandoffToTesterAsync("Created table cr_project", CancellationToken.None);

        Assert.Equal(AgentNames.Tester, state.ActiveAgentName);
    }

    [Fact]
    public async Task HandoffAsync_SavesProjectState()
    {
        var sessionId = "handoff-tester-2";
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new HandoffToTesterTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        await tool.HandoffToTesterAsync("Verify changes", CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, state, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandoffAsync_ReturnsConfirmationMessage()
    {
        var sessionId = "handoff-tester-3";
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new HandoffToTesterTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        var result = await tool.HandoffToTesterAsync("Created table", CancellationToken.None);

        Assert.Contains("Tester", result);
        Assert.Contains("Created table", result);
    }
}
