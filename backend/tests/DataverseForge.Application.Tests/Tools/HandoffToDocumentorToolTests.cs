using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class HandoffToDocumentorToolTests
{
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    [Fact]
    public async Task HandoffAsync_SetsActiveAgentToDocumentor()
    {
        var sessionId = "handoff-doc-1";
        var state = new ProjectState { ProjectId = sessionId, ActiveAgentName = AgentNames.SolutionArchitect };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new HandoffToDocumentorTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        await tool.HandoffToDocumentorAsync("Build and test complete", CancellationToken.None);

        Assert.Equal(AgentNames.Documentor, state.ActiveAgentName);
    }

    [Fact]
    public async Task HandoffAsync_SavesProjectState()
    {
        var sessionId = "handoff-doc-2";
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new HandoffToDocumentorTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        await tool.HandoffToDocumentorAsync("Document changes", CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, state, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandoffAsync_ReturnsConfirmationMessage()
    {
        var sessionId = "handoff-doc-3";
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new HandoffToDocumentorTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        var result = await tool.HandoffToDocumentorAsync("Created tables", CancellationToken.None);

        Assert.Contains("Documentor", result);
        Assert.Contains("Created tables", result);
    }
}
