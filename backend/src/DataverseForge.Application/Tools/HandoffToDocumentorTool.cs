using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;

namespace DataverseForge.Application.Tools;

public class HandoffToDocumentorTool(IProjectStateStore projectStateStore, ISessionContext sessionContext)
{
    [Description("Hand off the conversation to the Documentor agent to generate documentation. "
        + "Use this after a successful build and test cycle. "
        + "The Documentor will generate data dictionary, changelog, and ERD documentation.")]
    public async Task<string> HandoffToDocumentorAsync(
        [Description("Summary of what was built and tested that needs documentation")] string summary,
        CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.ActiveAgentName = AgentNames.Documentor;
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
        return $"Handoff to Documentor initiated. The Documentor will generate documentation. Summary: {summary}";
    }
}
