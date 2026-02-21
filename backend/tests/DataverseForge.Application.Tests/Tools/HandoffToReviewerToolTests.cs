using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class HandoffToReviewerToolTests
{
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    [Fact]
    public async Task HandoffAsync_SetsActiveAgentToReviewer()
    {
        var sessionId = "handoff-reviewer-1";
        var state = new ProjectState { ProjectId = sessionId, ActiveAgentName = AgentNames.SolutionArchitect };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new HandoffToReviewerTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        await tool.HandoffToReviewerAsync("{}", CancellationToken.None);

        Assert.Equal(AgentNames.Reviewer, state.ActiveAgentName);
    }

    [Fact]
    public async Task HandoffAsync_SavesProjectState()
    {
        var sessionId = "handoff-reviewer-2";
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new HandoffToReviewerTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        await tool.HandoffToReviewerAsync("{}", CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, state, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandoffAsync_ReturnsConfirmationMessage()
    {
        var sessionId = "handoff-reviewer-3";
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new HandoffToReviewerTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        var result = await tool.HandoffToReviewerAsync("{}", CancellationToken.None);

        Assert.Contains("Reviewer", result);
    }
}
