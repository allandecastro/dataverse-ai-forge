using DataverseForge.Application.Orchestration;
using DataverseForge.Application.Agents;

namespace DataverseForge.Application.Tests.Orchestration;

public class ChainLoopDecisionsTests
{
    private static readonly HashSet<string> AllAgents =
    [
        AgentNames.SolutionArchitect,
        AgentNames.BusinessAnalyst,
        AgentNames.DataModelBuilder,
        AgentNames.UIBuilder,
        AgentNames.DataPopulation,
        AgentNames.Tester,
    ];

    private static bool ExistsInAll(string name) => AllAgents.Contains(name);

    // ===== HAPPY PATH =====

    [Fact]
    public void HappyChain_SaToDmb_Continues()
    {
        var visited = new HashSet<string> { AgentNames.SolutionArchitect };

        var d = ChainLoopDecisions.Evaluate(
            currentAgent: AgentNames.SolutionArchitect,
            activeAgentAfter: AgentNames.DataModelBuilder,
            visitedAgents: visited,
            agentExists: ExistsInAll);

        Assert.Equal(ChainLoopDecisions.NextStep.Continue, d.Step);
        Assert.Equal(AgentNames.DataModelBuilder, d.NextActiveAgent);
        Assert.Null(d.AuditAction);
    }

    [Fact]
    public void HappyChain_DmbToTester_Continues()
    {
        var visited = new HashSet<string> { AgentNames.SolutionArchitect, AgentNames.DataModelBuilder };

        var d = ChainLoopDecisions.Evaluate(
            currentAgent: AgentNames.DataModelBuilder,
            activeAgentAfter: AgentNames.Tester,
            visitedAgents: visited,
            agentExists: ExistsInAll);

        Assert.Equal(ChainLoopDecisions.NextStep.Continue, d.Step);
        Assert.Equal(AgentNames.Tester, d.NextActiveAgent);
    }

    [Fact]
    public void NoHandoff_NormalAgent_StopsKeepingCurrent()
    {
        var visited = new HashSet<string> { AgentNames.SolutionArchitect };

        var d = ChainLoopDecisions.Evaluate(
            currentAgent: AgentNames.SolutionArchitect,
            activeAgentAfter: AgentNames.SolutionArchitect, // no change
            visitedAgents: visited,
            agentExists: ExistsInAll);

        Assert.Equal(ChainLoopDecisions.NextStep.Stop, d.Step);
        Assert.Equal(AgentNames.SolutionArchitect, d.NextActiveAgent);
        Assert.Null(d.AuditAction);
    }

    // ===== FORCED HANDOFF FOR TESTER =====
    // Symptom this prevents: Tester forgets to call handoff_to_solution_architect, then
    // user's next message gets routed to Tester (which violates STAY-IN-LANE).

    [Fact]
    public void Tester_NoHandoff_ForcedToSolutionArchitect()
    {
        var visited = new HashSet<string> { AgentNames.DataModelBuilder, AgentNames.Tester };

        var d = ChainLoopDecisions.Evaluate(
            currentAgent: AgentNames.Tester,
            activeAgentAfter: AgentNames.Tester, // didn't hand off
            visitedAgents: visited,
            agentExists: ExistsInAll);

        Assert.Equal(ChainLoopDecisions.NextStep.Stop, d.Step);
        Assert.Equal(AgentNames.SolutionArchitect, d.NextActiveAgent);
        Assert.Equal("ForcedHandoff", d.AuditAction);
        Assert.True(d.AuditSuccess);
    }

    // ===== CYCLE GUARD =====
    // Symptom this prevents: SA → DMB → Tester → SA → handoff_to_tester (already ran)
    // would silently leave activeAgent="Tester" and hijack the next user message.

    [Fact]
    public void Cycle_RestoresCurrentAgent()
    {
        var visited = new HashSet<string>
        {
            AgentNames.DataModelBuilder,
            AgentNames.Tester,
            AgentNames.SolutionArchitect, // SA already ran this turn
        };

        var d = ChainLoopDecisions.Evaluate(
            currentAgent: AgentNames.SolutionArchitect,
            activeAgentAfter: AgentNames.Tester, // SA tries to handoff to Tester again
            visitedAgents: visited,
            agentExists: ExistsInAll);

        Assert.Equal(ChainLoopDecisions.NextStep.Stop, d.Step);
        Assert.Equal(AgentNames.SolutionArchitect, d.NextActiveAgent); // restored, NOT Tester
    }

    // ===== TOPOLOGY VALIDATION =====
    // Symptom this prevents: builder → builder direct handoff (DMB → UIBuilder).

    [Fact]
    public void BuilderToBuilder_Blocked_RestoresCurrent()
    {
        var visited = new HashSet<string> { AgentNames.SolutionArchitect, AgentNames.DataModelBuilder };

        var d = ChainLoopDecisions.Evaluate(
            currentAgent: AgentNames.DataModelBuilder,
            activeAgentAfter: AgentNames.UIBuilder, // illegal handoff
            visitedAgents: visited,
            agentExists: ExistsInAll);

        Assert.Equal(ChainLoopDecisions.NextStep.Stop, d.Step);
        Assert.Equal(AgentNames.DataModelBuilder, d.NextActiveAgent);
        Assert.Equal("HandoffBlocked", d.AuditAction);
        Assert.False(d.AuditSuccess);
        Assert.NotNull(d.AuditDetail);
    }

    [Fact]
    public void GovernanceToBuilder_Blocked_RestoresCurrent()
    {
        var visited = new HashSet<string> { AgentNames.BusinessAnalyst };

        var d = ChainLoopDecisions.Evaluate(
            currentAgent: AgentNames.BusinessAnalyst,
            activeAgentAfter: AgentNames.DataModelBuilder, // BA can only go to SA
            visitedAgents: visited,
            agentExists: ExistsInAll);

        Assert.Equal(ChainLoopDecisions.NextStep.Stop, d.Step);
        Assert.Equal(AgentNames.BusinessAnalyst, d.NextActiveAgent);
        Assert.Equal("HandoffBlocked", d.AuditAction);
    }

    // ===== UNKNOWN AGENT =====

    [Fact]
    public void UnknownAgent_Blocked_RestoresCurrent()
    {
        var visited = new HashSet<string> { AgentNames.SolutionArchitect };
        bool exists(string n) => n != "GhostAgent";

        var d = ChainLoopDecisions.Evaluate(
            currentAgent: AgentNames.SolutionArchitect,
            activeAgentAfter: "GhostAgent",
            visitedAgents: visited,
            agentExists: exists);

        Assert.Equal(ChainLoopDecisions.NextStep.Stop, d.Step);
        Assert.Equal(AgentNames.SolutionArchitect, d.NextActiveAgent);
    }

    // ===== FULL CHAIN SIMULATION =====

    [Fact]
    public void FullChain_SaDmbTesterSa_AllValid()
    {
        // Step 1: SA → DMB
        var visited = new HashSet<string> { AgentNames.SolutionArchitect };
        var d1 = ChainLoopDecisions.Evaluate(
            AgentNames.SolutionArchitect, AgentNames.DataModelBuilder, visited, ExistsInAll);
        Assert.Equal(ChainLoopDecisions.NextStep.Continue, d1.Step);
        visited.Add(d1.NextActiveAgent);

        // Step 2: DMB → Tester
        var d2 = ChainLoopDecisions.Evaluate(
            AgentNames.DataModelBuilder, AgentNames.Tester, visited, ExistsInAll);
        Assert.Equal(ChainLoopDecisions.NextStep.Continue, d2.Step);
        visited.Add(d2.NextActiveAgent);

        // Step 3: Tester → SA — but SA already in visited! Cycle guard kicks in.
        var d3 = ChainLoopDecisions.Evaluate(
            AgentNames.Tester, AgentNames.SolutionArchitect, visited, ExistsInAll);
        Assert.Equal(ChainLoopDecisions.NextStep.Stop, d3.Step);
        Assert.Equal(AgentNames.Tester, d3.NextActiveAgent); // current preserved
    }
}
