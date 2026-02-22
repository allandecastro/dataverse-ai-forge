using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;

namespace DataverseForge.Application.Tools;

public class HandoffToUIBuilderTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Hand off the conversation to the UI Builder agent to create forms, views, dashboards, and model-driven apps. "
        + "Use this when you have finalized UI requirements and need to execute the user experience design in Dataverse. "
        + "Include the form/view specifications so the builder knows exactly what to create.")]
    public async Task<string> HandoffToUIBuilderAsync(
        [Description("The UI specification describing the forms, views, dashboards, and/or apps to create")] string uiSpec,
        CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.ActiveAgentName = AgentNames.UIBuilder;
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
        return "Handoff to UI Builder initiated with UI specification.";
    }
}
