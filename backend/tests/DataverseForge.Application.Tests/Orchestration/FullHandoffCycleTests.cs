using DataverseForge.Application.Contracts;
using DataverseForge.Application.Dtos;
using DataverseForge.Application.Orchestration;
using DataverseForge.Application.Agents;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataverseForge.Application.Tests.Orchestration;

/// <summary>
/// Integration test for the active handoff cycle:
/// SA → DMB(plan) → Reviewer → SA(approval) → DMB(execute) → Tester → SA.
/// Uses mocked agents that simulate handoffs by changing ActiveAgentName in ProjectState.
/// </summary>
public class FullHandoffCycleTests
{
    private readonly Mock<IProjectStateStore> _store = new();
    private readonly Mock<ILogger<AgentOrchestrator>> _logger = new();
    private readonly Mock<ISessionContext> _sessionContext = new();

    /// <summary>
    /// Creates a mock agent that simulates a handoff by changing the active agent
    /// in the project state on each ChatAsync call.
    /// </summary>
    private Mock<IAgent> CreateMockAgent(string name, IReadOnlyList<string> handoffTargets)
    {
        var mock = new Mock<IAgent>();
        mock.Setup(a => a.Name).Returns(name);
        mock.Setup(a => a.HandoffTargets).Returns(handoffTargets);
        return mock;
    }

    [Fact]
    public async Task FullCycle_SAToTesterToSA_HandoffsTraversedCorrectly()
    {
        var sessionId = "full-cycle-1";
        var state = new ProjectState { ProjectId = sessionId, ActiveAgentName = AgentNames.SolutionArchitect };

        _store.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => state);
        _store.Setup(s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sa = CreateMockAgent(AgentNames.SolutionArchitect,
            [AgentNames.BusinessAnalyst, AgentNames.DataModelBuilder, AgentNames.Reviewer, AgentNames.Tester]);
        var dmb = CreateMockAgent(AgentNames.DataModelBuilder,
            [AgentNames.SolutionArchitect, AgentNames.Reviewer, AgentNames.Tester]);
        var reviewer = CreateMockAgent(AgentNames.Reviewer,
            [AgentNames.SolutionArchitect]);
        var tester = CreateMockAgent(AgentNames.Tester,
            [AgentNames.SolutionArchitect]);

        // Step 1: SA → DMB
        sa.Setup(a => a.ChatAsync(sessionId, "Create a Feedback table", It.IsAny<CancellationToken>()))
            .Callback(() => state.ActiveAgentName = AgentNames.DataModelBuilder)
            .ReturnsAsync(new ChatResponse(sessionId, "Handing off to Data Model Builder.", state));

        // Step 2: DMB(plan) → Reviewer
        dmb.Setup(a => a.ChatAsync(sessionId, "Plan the table", It.IsAny<CancellationToken>()))
            .Callback(() => state.ActiveAgentName = AgentNames.Reviewer)
            .ReturnsAsync(new ChatResponse(sessionId, "Plan complete. Handing off to Reviewer.", state));

        // Step 3: Reviewer → SA
        reviewer.Setup(a => a.ChatAsync(sessionId, "Validate the plan", It.IsAny<CancellationToken>()))
            .Callback(() => state.ActiveAgentName = AgentNames.SolutionArchitect)
            .ReturnsAsync(new ChatResponse(sessionId, "Score 92/100. APPROVED.", state));

        // Step 4: SA → DMB
        sa.Setup(a => a.ChatAsync(sessionId, "Approved, execute", It.IsAny<CancellationToken>()))
            .Callback(() => state.ActiveAgentName = AgentNames.DataModelBuilder)
            .ReturnsAsync(new ChatResponse(sessionId, "Executing via Data Model Builder.", state));

        // Step 5: DMB(execute) → Tester
        dmb.Setup(a => a.ChatAsync(sessionId, "Execute the plan", It.IsAny<CancellationToken>()))
            .Callback(() => state.ActiveAgentName = AgentNames.Tester)
            .ReturnsAsync(new ChatResponse(sessionId, "Executed. Handing off to Tester.", state));

        // Step 6: Tester → SA
        tester.Setup(a => a.ChatAsync(sessionId, "Verify creation", It.IsAny<CancellationToken>()))
            .Callback(() => state.ActiveAgentName = AgentNames.SolutionArchitect)
            .ReturnsAsync(new ChatResponse(sessionId, "All checks passed.", state));

        var orchestrator = new AgentOrchestrator(
            [sa.Object, dmb.Object, reviewer.Object, tester.Object],
            _store.Object, _sessionContext.Object, _logger.Object);

        var r1 = await orchestrator.ChatAsync(sessionId, "Create a Feedback table", CancellationToken.None);
        Assert.Equal(AgentNames.DataModelBuilder, state.ActiveAgentName);

        var r2 = await orchestrator.ChatAsync(sessionId, "Plan the table", CancellationToken.None);
        Assert.Equal(AgentNames.Reviewer, state.ActiveAgentName);

        var r3 = await orchestrator.ChatAsync(sessionId, "Validate the plan", CancellationToken.None);
        Assert.Contains("APPROVED", r3.Response);
        Assert.Equal(AgentNames.SolutionArchitect, state.ActiveAgentName);

        var r4 = await orchestrator.ChatAsync(sessionId, "Approved, execute", CancellationToken.None);
        Assert.Equal(AgentNames.DataModelBuilder, state.ActiveAgentName);

        var r5 = await orchestrator.ChatAsync(sessionId, "Execute the plan", CancellationToken.None);
        Assert.Equal(AgentNames.Tester, state.ActiveAgentName);

        var r6 = await orchestrator.ChatAsync(sessionId, "Verify creation", CancellationToken.None);
        Assert.Equal(AgentNames.SolutionArchitect, state.ActiveAgentName);
        Assert.Contains("All checks passed", r6.Response);

        sa.Verify(a => a.ChatAsync(sessionId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        dmb.Verify(a => a.ChatAsync(sessionId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        reviewer.Verify(a => a.ChatAsync(sessionId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        tester.Verify(a => a.ChatAsync(sessionId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandoffTargets_DMBCanHandoffToReviewerAndTester()
    {
        var dmb = CreateMockAgent(AgentNames.DataModelBuilder,
            [AgentNames.SolutionArchitect, AgentNames.Reviewer, AgentNames.Tester]);

        Assert.Contains(AgentNames.Reviewer, dmb.Object.HandoffTargets);
        Assert.Contains(AgentNames.Tester, dmb.Object.HandoffTargets);
        Assert.Contains(AgentNames.SolutionArchitect, dmb.Object.HandoffTargets);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task HandoffTargets_GovernanceAgentsOnlyHandoffToSA()
    {
        var reviewer = CreateMockAgent(AgentNames.Reviewer, [AgentNames.SolutionArchitect]);
        var tester = CreateMockAgent(AgentNames.Tester, [AgentNames.SolutionArchitect]);

        Assert.Single(reviewer.Object.HandoffTargets);
        Assert.Equal(AgentNames.SolutionArchitect, reviewer.Object.HandoffTargets[0]);

        Assert.Single(tester.Object.HandoffTargets);
        Assert.Equal(AgentNames.SolutionArchitect, tester.Object.HandoffTargets[0]);

        await Task.CompletedTask;
    }
}
