using DataverseForge.Application.Agents;
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
        [Description("The UI specification describing the forms, views, dashboards, and/or apps to create")] string uiSpec = "",
        CancellationToken cancellationToken = default,
        [Description("ALIAS for `uiSpec`. Optional. Used only when `uiSpec` is empty.")] string reason = "",
        [Description("ALIAS for `uiSpec`. Optional. Used only when `uiSpec` and `reason` are empty.")] string summary = "",
        [Description("ALIAS for `uiSpec`. Optional. Used only when the others are empty.")] string context = "")
    {
        var effective = HandoffTool.PickEffectiveSpec(uiSpec, reason, summary, context);
        return await HandoffTool.ExecuteHandoffAsync(projectStateStore, sessionContext, AgentNames.UIBuilder, "UI Builder", effective, cancellationToken);
    }
}
