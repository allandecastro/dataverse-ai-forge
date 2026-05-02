using DataverseForge.Application.Agents;

namespace DataverseForge.Application.Orchestration;

/// <summary>
/// Pure decision logic for the orchestrator's chained-handoff loop. Lives outside
/// <c>OrchestratingChatClient</c> so it can be unit-tested without spinning up a chat
/// client / DI container. Symptoms when this logic regressed in the past:
///   - Tester forgot to handoff → next user message routed to Tester (STAY-IN-LANE break).
///   - SA → DMB → SA cycle leaked activeAgent="SA" into persisted state, breaking next turn.
///   - Unknown handoff target left activeAgent pointing at a dead agent.
/// </summary>
public static class ChainLoopDecisions
{
    public enum NextStep
    {
        /// <summary>Run the agent named <see cref="StepDecision.NextActiveAgent"/> next.</summary>
        Continue,
        /// <summary>Stop the chain. <see cref="StepDecision.NextActiveAgent"/> is what to persist.</summary>
        Stop
    }

    public record StepDecision(
        NextStep Step,
        string NextActiveAgent,
        string? AuditAction,
        string? AuditDetail,
        bool AuditSuccess);

    /// <summary>
    /// Evaluates what should happen at the end of an agent's turn given the persisted
    /// active-agent state, the agents we've visited this turn, and the topology
    /// validator. Pure — no side effects.
    /// </summary>
    /// <param name="currentAgent">Agent that just finished its stream.</param>
    /// <param name="activeAgentAfter">state.ActiveAgentName after the stream.</param>
    /// <param name="visitedAgents">Agents already executed this turn.</param>
    /// <param name="agentExists">Predicate for whether a given agent name is registered.</param>
    public static StepDecision Evaluate(
        string currentAgent,
        string activeAgentAfter,
        IReadOnlySet<string> visitedAgents,
        Func<string, bool> agentExists)
    {
        // CASE 1: Agent didn't hand off.
        if (activeAgentAfter == currentAgent)
        {
            // Tester/Reviewer must always hand back to SA — they can't be the
            // visible conversational agent. If they "forget", force the handoff in code so
            // the next user message routes to SA, not them.
            if (currentAgent == AgentNames.Tester
                || currentAgent == AgentNames.Reviewer)
            {
                return new StepDecision(
                    Step: NextStep.Stop,
                    NextActiveAgent: AgentNames.SolutionArchitect,
                    AuditAction: "ForcedHandoff",
                    AuditDetail: $"{currentAgent} did not hand off; auto-routed to SolutionArchitect",
                    AuditSuccess: true);
            }
            return new StepDecision(NextStep.Stop, currentAgent, null, null, true);
        }

        // CASE 2: Topology violation (e.g. builder → builder).
        var validation = HandoffValidator.Validate(currentAgent, activeAgentAfter);
        if (!validation.IsValid)
        {
            return new StepDecision(
                Step: NextStep.Stop,
                NextActiveAgent: currentAgent, // restore, don't honour the bad handoff
                AuditAction: "HandoffBlocked",
                AuditDetail: validation.ErrorMessage,
                AuditSuccess: false);
        }

        // CASE 3: Cycle — agent is trying to re-run someone we already ran this turn.
        // Common when SA blindly handoff_to_tester after Tester already PASSed. Without
        // this guard, the redundant handoff would persist activeAgent=Tester and the
        // next user message gets hijacked.
        if (visitedAgents.Contains(activeAgentAfter))
        {
            return new StepDecision(
                Step: NextStep.Stop,
                NextActiveAgent: currentAgent, // restore — don't leave state pointing at the cycled agent
                AuditAction: null,
                AuditDetail: null,
                AuditSuccess: true);
        }

        // CASE 4: Unknown agent. Refuse and restore.
        if (!agentExists(activeAgentAfter))
        {
            return new StepDecision(
                Step: NextStep.Stop,
                NextActiveAgent: currentAgent,
                AuditAction: null,
                AuditDetail: null,
                AuditSuccess: true);
        }

        // CASE 5: Valid handoff — continue the chain.
        return new StepDecision(NextStep.Continue, activeAgentAfter, null, null, true);
    }
}
