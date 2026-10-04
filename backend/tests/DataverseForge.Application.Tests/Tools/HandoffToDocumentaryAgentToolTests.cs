using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class HandoffToDocumentaryAgentToolTests
{
    [Fact]
    public async Task HandoffAsync_SetsActiveAgentToDocumentaryAgent()
    {
        var sessionId = "handoff-doc-test-1";
        var state = new ProjectState { ProjectId = sessionId, ActiveAgentName = AgentNames.BusinessAnalyst };

        var projectStateStore = new Mock<IProjectStateStore>();
        projectStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var sessionContext = new Mock<ISessionContext>();
        sessionContext.Setup(sc => sc.SessionId).Returns(sessionId);

        var tool = new HandoffToDocumentaryAgentTool(projectStateStore.Object, sessionContext.Object);
        await tool.HandoffToDocumentaryAgentAsync("ingest spec at C:/specs");

        Assert.Equal(AgentNames.DocumentaryAgent, state.ActiveAgentName);
    }

    [Fact]
    public async Task HandoffAsync_SavesProjectState()
    {
        var sessionId = "handoff-doc-test-2";
        var state = new ProjectState { ProjectId = sessionId };

        var projectStateStore = new Mock<IProjectStateStore>();
        projectStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var sessionContext = new Mock<ISessionContext>();
        sessionContext.Setup(sc => sc.SessionId).Returns(sessionId);

        var tool = new HandoffToDocumentaryAgentTool(projectStateStore.Object, sessionContext.Object);
        await tool.HandoffToDocumentaryAgentAsync("gap analysis spec vs backlog");

        projectStateStore.Verify(s => s.SaveAsync(sessionId, state, It.IsAny<CancellationToken>()), Times.Once);
    }
}
