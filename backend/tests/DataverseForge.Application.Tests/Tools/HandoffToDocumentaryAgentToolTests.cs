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

    [Fact]
    public async Task RepeatedKbRequests_AreNotTreatedAsARoutingLoop()
    {
        // Observed in the app: the 3rd ordinary KB request in a project was rejected as a
        // "routing loop" because each SA→DocAgent→SA round trip counted as reversals.
        var sessionId = "handoff-doc-loop";
        var state = new ProjectState { ProjectId = sessionId, ActiveAgentName = AgentNames.SolutionArchitect };
        var store = new Mock<IProjectStateStore>();
        store.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);
        var runId = 0;
        var session = new Mock<ISessionContext>();
        session.Setup(sc => sc.SessionId).Returns(sessionId);
        session.Setup(sc => sc.RunId).Returns(() => $"run-{runId}");

        var toDoc = new HandoffToDocumentaryAgentTool(store.Object, session.Object);
        var toSa = new HandoffToSolutionArchitectTool(store.Object, session.Object);

        for (runId = 1; runId <= 5; runId++)
        {
            state.ActiveAgentName = AgentNames.SolutionArchitect;
            var go = await toDoc.HandoffToDocumentaryAgentAsync($"kb request {runId}");
            Assert.DoesNotContain("REJECTED", go);

            state.ActiveAgentName = AgentNames.DocumentaryAgent;
            var back = await toSa.HandoffToSolutionArchitectAsync($"done {runId}");
            Assert.DoesNotContain("REJECTED", back);
        }
    }
}
