using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class HandoffToSolutionArchitectToolTests
{
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    [Fact]
    public async Task HandoffAsync_SetsActiveAgentToSolutionArchitect()
    {
        var sessionId = "handoff-sa-test-1";
        var state = new ProjectState { ProjectId = sessionId, ActiveAgentName = AgentNames.BusinessAnalyst };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new HandoffToSolutionArchitectTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        await tool.HandoffToSolutionArchitectAsync("Requirements gathered", CancellationToken.None);

        Assert.Equal(AgentNames.SolutionArchitect, state.ActiveAgentName);
    }

    [Fact]
    public async Task HandoffAsync_SavesProjectState()
    {
        var sessionId = "handoff-sa-test-2";
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new HandoffToSolutionArchitectTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        await tool.HandoffToSolutionArchitectAsync("Spec complete", CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, state, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandoffAsync_ReturnsConfirmationMessage()
    {
        var sessionId = "handoff-sa-test-3";
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new HandoffToSolutionArchitectTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        var result = await tool.HandoffToSolutionArchitectAsync("Requirements ready", CancellationToken.None);

        Assert.Contains("Solution Architect", result);
    }
}
