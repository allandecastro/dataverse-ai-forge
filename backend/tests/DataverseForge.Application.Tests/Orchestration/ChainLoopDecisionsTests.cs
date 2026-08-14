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
        AgentNames.DocumentaryAgent,
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

    // ===== FORCED HANDOFF TO TESTER FOR BUILDERS =====
    // Symptom: builder calls execute_* and then forgets handoff_to_tester. The
    // orchestrator should force the handoff so verification is structural.

    [Fact]
    public void Builder_ExecutedWrite_NoHandoff_NoPendingPlans_ForcesTesterHandoff()
    {
        var visited = new HashSet<string> { AgentNames.SolutionArchitect, AgentNames.DataModelBuilder };

        var d = ChainLoopDecisions.Evaluate(
            currentAgent: AgentNames.DataModelBuilder,
            activeAgentAfter: AgentNames.DataModelBuilder, // didn't hand off
            visitedAgents: visited,
            agentExists: ExistsInAll,
            builderExecutedWrite: true,
            hasPendingPlans: false);

        Assert.Equal(ChainLoopDecisions.NextStep.Continue, d.Step);
        Assert.Equal(AgentNames.Tester, d.NextActiveAgent);
        Assert.Equal("ForcedHandoffToTester", d.AuditAction);
    }

    // Symptom this prevents (2026-05-25 trace): DMB planned 2 tables + 6 fields,
    // executed only the tables, asked the user "execute fields or discard?", then
    // the forced handoff fired Tester anyway → FAIL reports for fields the user
    // hadn't even chosen to skip yet.
    [Fact]
    public void Builder_ExecutedWrite_WithPendingPlans_DoesNotForceTesterHandoff()
    {
        var visited = new HashSet<string> { AgentNames.SolutionArchitect, AgentNames.DataModelBuilder };

        var d = ChainLoopDecisions.Evaluate(
            currentAgent: AgentNames.DataModelBuilder,
            activeAgentAfter: AgentNames.DataModelBuilder, // didn't hand off
            visitedAgents: visited,
            agentExists: ExistsInAll,
            builderExecutedWrite: true,
            hasPendingPlans: true); // partial execution mid-batch

        // Stop here — the builder's own recovery message ("execute fields
        // or discard?") is what the user needs to see, not Tester FAIL reports.
        Assert.Equal(ChainLoopDecisions.NextStep.Stop, d.Step);
        Assert.Equal(AgentNames.DataModelBuilder, d.NextActiveAgent);
        Assert.Equal("SkippedForcedHandoffToTester", d.AuditAction);
        Assert.True(d.AuditSuccess);
    }

    [Fact]
    public void Builder_NoExecutedWrite_NoHandoff_StopsWithoutForcing()
    {
        // Read-only turn — builder didn't write anything. No Tester involvement.
        var visited = new HashSet<string> { AgentNames.SolutionArchitect, AgentNames.DataModelBuilder };

        var d = ChainLoopDecisions.Evaluate(
            currentAgent: AgentNames.DataModelBuilder,
            activeAgentAfter: AgentNames.DataModelBuilder,
            visitedAgents: visited,
            agentExists: ExistsInAll,
            builderExecutedWrite: false,
            hasPendingPlans: false);

        Assert.Equal(ChainLoopDecisions.NextStep.Stop, d.Step);
        Assert.Equal(AgentNames.DataModelBuilder, d.NextActiveAgent);
        Assert.Null(d.AuditAction);
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

    // Non-Tester re-entry is a legitimate 2nd phase: Data Population creates the
    // Events, Tester verifies them, SA routes back to Data Population to create the
    // dependent Sessions (which need the Events' real GUIDs). Blanket-blocking that
    // dropped the Sessions entirely. Allow it — MaxChainedAgents + the handoff
    // reversal guard bound any real loop.
    [Fact]
    public void Reentry_NonTester_Continues()
    {
        var visited = new HashSet<string>
        {
            AgentNames.DataPopulation, // already ran phase 1 (Events)
            AgentNames.Tester,
            AgentNames.SolutionArchitect,
        };

        var d = ChainLoopDecisions.Evaluate(
            currentAgent: AgentNames.SolutionArchitect,
            activeAgentAfter: AgentNames.DataPopulation, // route back for phase 2 (Sessions)
            visitedAgents: visited,
            agentExists: ExistsInAll);

        Assert.Equal(ChainLoopDecisions.NextStep.Continue, d.Step);
        Assert.Equal(AgentNames.DataPopulation, d.NextActiveAgent);
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

        // Step 3: Tester → SA — SA already ran this turn, but SA is non-Tester so
        // re-entry is now ALLOWED (SA re-enters to report the verification result
        // and suggest next steps). Bounded by MaxChainedAgents + the reversal guard.
        // Only re-entering the Tester itself is blocked (see Cycle_RestoresCurrentAgent).
        var d3 = ChainLoopDecisions.Evaluate(
            AgentNames.Tester, AgentNames.SolutionArchitect, visited, ExistsInAll);
        Assert.Equal(ChainLoopDecisions.NextStep.Continue, d3.Step);
        Assert.Equal(AgentNames.SolutionArchitect, d3.NextActiveAgent);
    }

    // ── Documentary Agent ──────────────────────────────────────────────────────

    [Fact]
    public void DocumentaryAgent_ForgetsHandoff_ForcedToSA()
    {
        var visited = new HashSet<string> { AgentNames.SolutionArchitect, AgentNames.DocumentaryAgent };

        var d = ChainLoopDecisions.Evaluate(
            currentAgent: AgentNames.DocumentaryAgent,
            activeAgentAfter: AgentNames.DocumentaryAgent, // forgot to handoff
            visitedAgents: visited,
            agentExists: ExistsInAll);

        Assert.Equal(ChainLoopDecisions.NextStep.Stop, d.Step);
        Assert.Equal(AgentNames.SolutionArchitect, d.NextActiveAgent);
        Assert.Equal("ForcedHandoff", d.AuditAction);
    }

    [Fact]
    public void DocumentaryAgent_HandsOffToSA_Continues()
    {
        var visited = new HashSet<string> { AgentNames.SolutionArchitect, AgentNames.DocumentaryAgent };

        var d = ChainLoopDecisions.Evaluate(
            currentAgent: AgentNames.DocumentaryAgent,
            activeAgentAfter: AgentNames.SolutionArchitect,
            visitedAgents: visited,
            agentExists: ExistsInAll);

        Assert.Equal(ChainLoopDecisions.NextStep.Continue, d.Step);
        Assert.Equal(AgentNames.SolutionArchitect, d.NextActiveAgent);
    }

    [Fact]
    public void SA_HandsOffToDocumentaryAgent_Continues()
    {
        var visited = new HashSet<string> { AgentNames.SolutionArchitect };

        var d = ChainLoopDecisions.Evaluate(
            currentAgent: AgentNames.SolutionArchitect,
            activeAgentAfter: AgentNames.DocumentaryAgent,
            visitedAgents: visited,
            agentExists: ExistsInAll);

        Assert.Equal(ChainLoopDecisions.NextStep.Continue, d.Step);
        Assert.Equal(AgentNames.DocumentaryAgent, d.NextActiveAgent);
    }
}
