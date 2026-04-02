using DataverseForge.Application.Contracts;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Shared handoff logic. Individual HandoffTo* tools delegate to this.
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
        state.ActiveAgentName = targetAgentName;
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
        return $"Handoff to {targetDisplayName} initiated. Reason: {reason}";
    }
}
