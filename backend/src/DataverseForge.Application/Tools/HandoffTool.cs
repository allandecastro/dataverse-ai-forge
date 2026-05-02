using DataverseForge.Application.Contracts;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Shared handoff logic. Individual HandoffTo* tools delegate to this.
///
/// One handoff per turn — code-enforced. The agent kept calling
/// HandoffToUIBuilder + HandoffToTester in the same turn; the last call won
/// the ActiveAgentName race and the Tester ran on a build that hadn't happened
/// yet. The prompt rule wasn't being followed reliably, so we lock it down at
/// the tool layer: the first handoff in a turn (identified by RunId) sets
/// LastHandoffRunId; subsequent handoff calls in the same RunId are rejected.
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
        if (!string.IsNullOrEmpty(currentRunId)
            && string.Equals(state.LastHandoffRunId, currentRunId, StringComparison.Ordinal))
        {
            return $"REJECTED: A handoff was already issued in this turn (to '{state.ActiveAgentName}'). "
                + "Only ONE handoff is allowed per turn — wait for the next agent's response before "
                + $"chaining another. Skipping this handoff to '{targetDisplayName}'.";
        }

        state.ActiveAgentName = targetAgentName;
        state.LastHandoffRunId = currentRunId ?? string.Empty;
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
        return $"Handoff to {targetDisplayName} initiated. Reason: {reason}";
    }
}
