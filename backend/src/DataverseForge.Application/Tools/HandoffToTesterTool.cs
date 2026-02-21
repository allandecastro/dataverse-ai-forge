using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;

namespace DataverseForge.Application.Tools;

public class HandoffToTesterTool(IProjectStateStore projectStateStore, ISessionContext sessionContext)
{
    [Description("Hand off the conversation to the Tester agent for post-execution verification. "
        + "Use this after a builder agent has executed changes in Dataverse. "
        + "The Tester will re-read components from the API and verify they match the planned specification.")]
    public async Task<string> HandoffToTesterAsync(
        [Description("Summary of what was created or modified that needs verification")] string summary,
        CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.ActiveAgentName = AgentNames.Tester;
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
        return $"Handoff to Tester initiated. The Tester will verify the executed changes. Summary: {summary}";
    }
}
