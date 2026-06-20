using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

/// <summary>
/// F-23 (bug pattern #8): HandoffTool must mutate state via ApplyAndSaveAsync,
/// which re-reads the latest ProjectState immediately before saving. That way a
/// concurrent write (parallel agent action, SSE audit emitter) that landed
/// between the top-of-method read and the save isn't silently clobbered.
/// </summary>
public class HandoffToolRaceTests
{
    [Fact]
    public async Task ExecuteHandoff_AppliesToFreshlyReadState_NotTheStaleCopy()
    {
        var sessionId = "handoff-race-1";
        var staleState = new ProjectState { ProjectId = sessionId, ActiveAgentName = AgentNames.SolutionArchitect };
        var freshState = new ProjectState { ProjectId = sessionId, ActiveAgentName = AgentNames.SolutionArchitect };

        var store = new Mock<IProjectStateStore>();
        var ctx = new Mock<ISessionContext>();
        ctx.Setup(c => c.SessionId).Returns(sessionId);
        ctx.Setup(c => c.RunId).Returns("run-1");

        // First read (top of method) returns the stale copy; ApplyAndSaveAsync's
        // re-read returns the fresh copy — simulating a concurrent write that
        // landed during the window.
        store.SetupSequence(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(staleState)
            .ReturnsAsync(freshState);

        await HandoffTool.ExecuteHandoffAsync(
            store.Object, ctx.Object,
            AgentNames.Tester, "Tester", "verify the changes", CancellationToken.None);

        // The freshly-read copy is the one mutated + saved; the stale copy is untouched.
        Assert.Equal(AgentNames.Tester, freshState.ActiveAgentName);
        Assert.Equal(AgentNames.SolutionArchitect, staleState.ActiveAgentName);
        store.Verify(s => s.SaveAsync(sessionId, freshState, It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.SaveAsync(sessionId, staleState, It.IsAny<CancellationToken>()), Times.Never);
    }
}
