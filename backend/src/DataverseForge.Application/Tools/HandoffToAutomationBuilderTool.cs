using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;

namespace DataverseForge.Application.Tools;

public class HandoffToAutomationBuilderTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Hand off the conversation to the Automation Builder agent to create plugins, BPFs, business rules, and custom APIs. "
        + "Use this when you have finalized automation requirements and need to implement business logic in Dataverse. "
        + "Include the automation specification so the builder knows exactly what to create.")]
    public async Task<string> HandoffToAutomationBuilderAsync(
        [Description("The automation specification describing the plugins, BPFs, business rules, and/or custom APIs to create")] string automationSpec,
        CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.ActiveAgentName = AgentNames.AutomationBuilder;
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
        return "Handoff to Automation Builder initiated with automation specification.";
    }
}
