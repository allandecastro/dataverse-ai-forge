using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class HandoffToSecurityBuilderToolTests
{
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    [Fact]
    public async Task HandoffAsync_SetsActiveAgentToSecurityBuilder()
    {
        var sessionId = "handoff-sb-1";
        var state = new ProjectState { ProjectId = sessionId, ActiveAgentName = AgentNames.SolutionArchitect };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new HandoffToSecurityBuilderTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        await tool.HandoffToSecurityBuilderAsync("Create security roles for Work Order", CancellationToken.None);

        Assert.Equal(AgentNames.SecurityBuilder, state.ActiveAgentName);
    }

    [Fact]
    public async Task HandoffAsync_SavesProjectState()
    {
        var sessionId = "handoff-sb-2";
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new HandoffToSecurityBuilderTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        await tool.HandoffToSecurityBuilderAsync("Security spec", CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, state, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandoffAsync_ReturnsConfirmationMessage()
    {
        var sessionId = "handoff-sb-3";
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new HandoffToSecurityBuilderTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        var result = await tool.HandoffToSecurityBuilderAsync("Security spec", CancellationToken.None);

        Assert.Contains("Security Builder", result);
    }
}
