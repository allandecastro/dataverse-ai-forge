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
    /// <param name="builderExecutedWrite">
    /// True iff the agent that just finished is a builder AND its turn added a
    /// new entry to <c>ExecutedPlans</c> (i.e. an Execute* tool succeeded).
    /// When this is true AND the builder forgot to call <c>handoff_to_tester</c>,
    /// the orchestrator forces a Tester handoff so verification is structural
    /// and not dependent on the model remembering the tool call. Real-session
    /// pain from the 2026-05-13/14/15/16 traces: builders frequently say
    /// "I am handing off to Tester" in chat without firing the tool, and
    /// the conversation stalls until the user nudges.
    /// </param>
    /// <param name="hasPendingPlans">
    /// True iff <c>state.PendingPlans</c> is non-empty at end of the builder's
    /// turn. When true AND the builder partially executed (e.g. created tables
    /// but never asked for approval on the planned fields), the forced Tester
    /// handoff is SKIPPED — Tester would only PASS the executed bits and FAIL
    /// the rest, mid-conversation, before the user even sees the builder's
    /// recovery question. Real-session pain from the 2026-05-25 trace:
    /// DMB planned 2 tables + 6 fields, executed only the tables, asked the
    /// user "execute fields or discard?", and Tester ran 9s later anyway —
    /// flooding the chat with FAIL reports for fields the user hadn't even
    /// chosen to skip yet. The builder's own recovery message is the right
    /// behaviour; the orchestrator must not pre-empt it.
    /// </param>
    public static StepDecision Evaluate(
        string currentAgent,
        string activeAgentAfter,
        IReadOnlySet<string> visitedAgents,
        Func<string, bool> agentExists,
        bool builderExecutedWrite = false,
        bool hasPendingPlans = false)
    {
        // CASE 1: Agent didn't hand off.
        if (activeAgentAfter == currentAgent)
        {
            // Tester and service agents (e.g. DocumentaryAgent) must always hand
            // back to SA — they can't be the visible conversational agent.
            // If they "forget", force the handoff in code so the next user
            // message routes to SA, not to the service/tester agent.
            if (currentAgent == AgentNames.Tester || AgentNames.ServiceAgents.Contains(currentAgent))
            {
                return new StepDecision(
                    Step: NextStep.Stop,
                    NextActiveAgent: AgentNames.SolutionArchitect,
                    AuditAction: "ForcedHandoff",
                    AuditDetail: $"{currentAgent} did not hand off; auto-routed to SolutionArchitect",
                    AuditSuccess: true);
            }

            // Structural Tester auto-handoff for builders that wrote but didn't
            // hand off. Verification is no longer dependent on the model's
            // willingness to call handoff_to_tester.
            //
            // EXCEPTION: if the builder still has pending plans, the partial
            // execution is intentional (they're mid-batch and waiting for the
            // user to decide on the rest). Running Tester now would emit FAIL
            // reports for the un-executed plans and confuse the conversation.
            // Let the builder's own recovery message stand.
            if (builderExecutedWrite && IsBuilder(currentAgent) && !hasPendingPlans)
            {
                return new StepDecision(
                    Step: NextStep.Continue,
                    NextActiveAgent: AgentNames.Tester,
                    AuditAction: "ForcedHandoffToTester",
                    AuditDetail: $"{currentAgent} executed a write without calling handoff_to_tester; auto-routed to Tester",
                    AuditSuccess: true);
            }

            if (builderExecutedWrite && IsBuilder(currentAgent) && hasPendingPlans)
            {
                return new StepDecision(
                    Step: NextStep.Stop,
                    NextActiveAgent: currentAgent,
                    AuditAction: "SkippedForcedHandoffToTester",
                    AuditDetail: $"{currentAgent} executed a write but still has pending plans; deferring Tester handoff to the builder's recovery message",
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

        // CASE 3: Re-entry — agent is handing back to someone already run this turn.
        // Re-entering the TESTER after it already verified is the classic leak (SA
        // blindly handoff_to_tester after a PASS) — block it and restore, else the
        // redundant handoff persists activeAgent=Tester and the next user message
        // gets hijacked.
        //
        // But a NON-Tester re-entry is often a LEGITIMATE second phase: e.g. Data
        // Population creates the Events, hands to Tester to verify, SA routes back
        // to Data Population to create the dependent Sessions (which need the
        // Events' real GUIDs). Blanket-blocking that dropped the Sessions entirely.
        // Allow non-Tester re-entry; the MaxChainedAgents cap in the orchestrator
        // AND the cross-turn handoff-reversal guard in HandoffTool still bound any
        // real ping-pong.
        if (activeAgentAfter == AgentNames.Tester && visitedAgents.Contains(activeAgentAfter))
        {
            return new StepDecision(
                Step: NextStep.Stop,
                NextActiveAgent: currentAgent, // restore — don't leave state pointing at the cycled Tester
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

    private static bool IsBuilder(string agentName) => AgentNames.Builders.Contains(agentName);
}
