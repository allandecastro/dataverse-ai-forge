using DataverseForge.Application.Contracts;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Shared handoff logic. Individual HandoffTo* tools delegate to this.
///
/// ONE HANDOFF PER AGENT PER TURN — code-enforced. The original guard rejected
/// any second handoff in the same RunId, but legitimate routing chains
/// (UIBuilder → SA → DMB) all happen in one RunId — three different agents
/// each issuing one handoff. We now key on (RunId, FromAgent): the SAME agent
/// firing two handoffs in one turn is rejected; different agents in the same
/// turn are fine.
///
/// Past failure being prevented: SA emitting parallel HandoffToUIBuilder +
/// HandoffToTester in a single response, racing the ActiveAgentName field.
/// </summary>
public static class HandoffTool
{
    public static async Task<string> ExecuteHandoffAsync(
        IProjectStateStore projectStateStore,
        ISessionContext sessionContext,
        string targetAgentName,
        string targetDisplayName,
        string reason,
        CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        var currentRunId = sessionContext.RunId;
        var currentAgent = state.ActiveAgentName;

        var sameRunId = !string.IsNullOrEmpty(currentRunId)
            && string.Equals(state.LastHandoffRunId, currentRunId, StringComparison.Ordinal);
        var sameAgent = !string.IsNullOrEmpty(currentAgent)
            && string.Equals(state.LastHandoffFromAgent, currentAgent, StringComparison.Ordinal);

        if (sameRunId && sameAgent)
        {
            return $"REJECTED: Agent '{currentAgent}' already issued a handoff in this turn (to '{state.ActiveAgentName}'). "
                + "An agent can only issue ONE handoff per turn — wait for the next agent's response before "
                + $"chaining another. Skipping this handoff to '{targetDisplayName}'.";
        }

        state.ActiveAgentName = targetAgentName;
        state.LastHandoffRunId = currentRunId ?? string.Empty;
        state.LastHandoffFromAgent = currentAgent ?? string.Empty;
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
        return $"Handoff to {targetDisplayName} initiated. Reason: {reason}";
    }
}
