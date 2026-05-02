using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Application.Agents;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class HandoffToDataPopulationToolTests
{
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    [Fact]
    public async Task HandoffAsync_SetsActiveAgentToDataPopulation()
    {
        var sessionId = "handoff-dp-1";
        var state = new ProjectState { ProjectId = sessionId, ActiveAgentName = AgentNames.SolutionArchitect };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new HandoffToDataPopulationTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        await tool.HandoffToDataPopulationAsync("Seed reference data for Work Order", CancellationToken.None);

        Assert.Equal(AgentNames.DataPopulation, state.ActiveAgentName);
    }

    [Fact]
    public async Task HandoffAsync_SavesProjectState()
    {
        var sessionId = "handoff-dp-2";
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new HandoffToDataPopulationTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        await tool.HandoffToDataPopulationAsync("Test data generation", CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, state, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandoffAsync_ReturnsConfirmationMessage()
    {
        var sessionId = "handoff-dp-3";
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new HandoffToDataPopulationTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        var result = await tool.HandoffToDataPopulationAsync("Test data generation", CancellationToken.None);

        Assert.Contains("Data Population", result);
    }
}
