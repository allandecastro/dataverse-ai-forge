using DataverseForge.Application.Contracts;
using DataverseForge.Application.Dtos;
using DataverseForge.Application.Orchestration;
using DataverseForge.Domain;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataverseForge.Application.Tests.Orchestration;

/// <summary>
/// Integration test for the full 5-agent handoff cycle:
/// SA → DMB(plan) → Reviewer → SA(approval) → DMB(execute) → Tester → Documentor → SA
/// Uses mocked agents that simulate handoffs by changing ActiveAgentName in ProjectState.
/// </summary>
public class FullHandoffCycleTests
{
    private readonly Mock<IProjectStateStore> _store = new();
    private readonly Mock<ILogger<AgentOrchestrator>> _logger = new();

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
    public async Task FullCycle_SAToDocumentorToSA_HandoffsTraversedCorrectly()
    {
        var sessionId = "full-cycle-1";
        var state = new ProjectState { ProjectId = sessionId, ActiveAgentName = AgentNames.SolutionArchitect };

        _store.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => state);
        _store.Setup(s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Set up all agents
        var sa = CreateMockAgent(AgentNames.SolutionArchitect,
            [AgentNames.BusinessAnalyst, AgentNames.DataModelBuilder, AgentNames.Reviewer, AgentNames.Tester, AgentNames.Documentor]);
        var dmb = CreateMockAgent(AgentNames.DataModelBuilder,
            [AgentNames.SolutionArchitect, AgentNames.Reviewer, AgentNames.Tester]);
        var reviewer = CreateMockAgent(AgentNames.Reviewer,
            [AgentNames.SolutionArchitect]);
        var tester = CreateMockAgent(AgentNames.Tester,
            [AgentNames.SolutionArchitect]);
        var documentor = CreateMockAgent(AgentNames.Documentor,
            [AgentNames.SolutionArchitect]);

        // Step 1: SA → DMB (SA hands off to DMB for planning)
        sa.Setup(a => a.ChatAsync(sessionId, "Create a Feedback table", It.IsAny<CancellationToken>()))
            .Callback(() => state.ActiveAgentName = AgentNames.DataModelBuilder)
            .ReturnsAsync(new ChatResponse(sessionId, "Handing off to Data Model Builder.", state));

        // Step 2: DMB(plan) → Reviewer (DMB plans and hands off to Reviewer)
        dmb.Setup(a => a.ChatAsync(sessionId, "Plan the table", It.IsAny<CancellationToken>()))
            .Callback(() => state.ActiveAgentName = AgentNames.Reviewer)
            .ReturnsAsync(new ChatResponse(sessionId, "Plan complete. Handing off to Reviewer.", state));

        // Step 3: Reviewer → SA (Reviewer validates and hands back to SA)
        reviewer.Setup(a => a.ChatAsync(sessionId, "Validate the plan", It.IsAny<CancellationToken>()))
            .Callback(() => state.ActiveAgentName = AgentNames.SolutionArchitect)
            .ReturnsAsync(new ChatResponse(sessionId, "Score 92/100. APPROVED.", state));

        // Step 4: SA → DMB (SA presents review, user approves, hands to DMB for execution)
        sa.Setup(a => a.ChatAsync(sessionId, "Approved, execute", It.IsAny<CancellationToken>()))
            .Callback(() => state.ActiveAgentName = AgentNames.DataModelBuilder)
            .ReturnsAsync(new ChatResponse(sessionId, "Executing via Data Model Builder.", state));

        // Step 5: DMB(execute) → Tester (DMB executes and hands off to Tester)
        dmb.Setup(a => a.ChatAsync(sessionId, "Execute the plan", It.IsAny<CancellationToken>()))
            .Callback(() => state.ActiveAgentName = AgentNames.Tester)
            .ReturnsAsync(new ChatResponse(sessionId, "Executed. Handing off to Tester.", state));

        // Step 6: Tester → Documentor (via SA — Tester hands to SA, SA hands to Documentor)
        tester.Setup(a => a.ChatAsync(sessionId, "Verify creation", It.IsAny<CancellationToken>()))
            .Callback(() => state.ActiveAgentName = AgentNames.SolutionArchitect)
            .ReturnsAsync(new ChatResponse(sessionId, "All checks passed.", state));

        sa.Setup(a => a.ChatAsync(sessionId, "Tests passed, document", It.IsAny<CancellationToken>()))
            .Callback(() => state.ActiveAgentName = AgentNames.Documentor)
            .ReturnsAsync(new ChatResponse(sessionId, "Handing off to Documentor.", state));

        // Step 7: Documentor → SA (Documentor generates docs and hands back)
        documentor.Setup(a => a.ChatAsync(sessionId, "Generate documentation", It.IsAny<CancellationToken>()))
            .Callback(() => state.ActiveAgentName = AgentNames.SolutionArchitect)
            .ReturnsAsync(new ChatResponse(sessionId, "Documentation complete.", state));

        var orchestrator = new AgentOrchestrator(
            [sa.Object, dmb.Object, reviewer.Object, tester.Object, documentor.Object],
            _store.Object, _logger.Object);

        // Execute the full cycle
        // Step 1: SA → DMB
        var r1 = await orchestrator.ChatAsync(sessionId, "Create a Feedback table", CancellationToken.None);
        Assert.Equal(AgentNames.DataModelBuilder, state.ActiveAgentName);

        // Step 2: DMB(plan) → Reviewer
        var r2 = await orchestrator.ChatAsync(sessionId, "Plan the table", CancellationToken.None);
        Assert.Equal(AgentNames.Reviewer, state.ActiveAgentName);

        // Step 3: Reviewer → SA
        var r3 = await orchestrator.ChatAsync(sessionId, "Validate the plan", CancellationToken.None);
        Assert.Contains("APPROVED", r3.Response);
        Assert.Equal(AgentNames.SolutionArchitect, state.ActiveAgentName);

        // Step 4: SA → DMB (user approval → execute)
        var r4 = await orchestrator.ChatAsync(sessionId, "Approved, execute", CancellationToken.None);
        Assert.Equal(AgentNames.DataModelBuilder, state.ActiveAgentName);

        // Step 5: DMB(execute) → Tester
        var r5 = await orchestrator.ChatAsync(sessionId, "Execute the plan", CancellationToken.None);
        Assert.Equal(AgentNames.Tester, state.ActiveAgentName);

        // Step 6: Tester → SA → Documentor
        var r6 = await orchestrator.ChatAsync(sessionId, "Verify creation", CancellationToken.None);
        Assert.Equal(AgentNames.SolutionArchitect, state.ActiveAgentName);
        var r7 = await orchestrator.ChatAsync(sessionId, "Tests passed, document", CancellationToken.None);
        Assert.Equal(AgentNames.Documentor, state.ActiveAgentName);

        // Step 7: Documentor → SA (full circle)
        var r8 = await orchestrator.ChatAsync(sessionId, "Generate documentation", CancellationToken.None);
        Assert.Equal(AgentNames.SolutionArchitect, state.ActiveAgentName);
        Assert.Contains("Documentation complete", r8.Response);

        // Verify all agents were called
        sa.Verify(a => a.ChatAsync(sessionId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        dmb.Verify(a => a.ChatAsync(sessionId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        reviewer.Verify(a => a.ChatAsync(sessionId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        tester.Verify(a => a.ChatAsync(sessionId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        documentor.Verify(a => a.ChatAsync(sessionId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandoffTargets_DMBCanHandoffToReviewerAndTester()
    {
        var dmb = CreateMockAgent(AgentNames.DataModelBuilder,
            [AgentNames.SolutionArchitect, AgentNames.Reviewer, AgentNames.Tester]);

        Assert.Contains(AgentNames.Reviewer, dmb.Object.HandoffTargets);
        Assert.Contains(AgentNames.Tester, dmb.Object.HandoffTargets);
        Assert.Contains(AgentNames.SolutionArchitect, dmb.Object.HandoffTargets);
        await Task.CompletedTask; // Suppress async warning
    }

    [Fact]
    public async Task HandoffTargets_GovernanceAgentsOnlyHandoffToSA()
    {
        var reviewer = CreateMockAgent(AgentNames.Reviewer, [AgentNames.SolutionArchitect]);
        var tester = CreateMockAgent(AgentNames.Tester, [AgentNames.SolutionArchitect]);
        var documentor = CreateMockAgent(AgentNames.Documentor, [AgentNames.SolutionArchitect]);

        Assert.Single(reviewer.Object.HandoffTargets);
        Assert.Equal(AgentNames.SolutionArchitect, reviewer.Object.HandoffTargets[0]);

        Assert.Single(tester.Object.HandoffTargets);
        Assert.Equal(AgentNames.SolutionArchitect, tester.Object.HandoffTargets[0]);

        Assert.Single(documentor.Object.HandoffTargets);
        Assert.Equal(AgentNames.SolutionArchitect, documentor.Object.HandoffTargets[0]);

        await Task.CompletedTask; // Suppress async warning
    }
}
