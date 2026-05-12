using DataverseForge.Application.Agents;
using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;

namespace DataverseForge.Application.Tools;

public class HandoffToAutomationBuilderTool(IProjectStateStore projectStateStore, ISessionContext sessionContext)
{
    [Description("Hand off the conversation to the Automation Builder agent for "
        + "Business Process Flow (BPF) + Business Rule lifecycle work — create / update / "
        + "delete / activate / deactivate. v1 supports single-entity linear BPFs and "
        + "single-condition single-action Business Rules; anything outside that scope "
        + "(multi-entity BPFs, multi-action rules, AND/OR conditions, Power Automate flows, "
        + "classic workflows, custom actions, plug-ins) is REFUSED with a maker-portal pointer. "
        + "Use this when the user asks for conditional show/hide, set required, lock, set "
        + "value behavior on form fields (Business Rule) OR a multi-stage process attached "
        + "to a table (BPF).")]
    public async Task<string> HandoffToAutomationBuilderAsync(
        [Description("The automation specification — what BPF or Business Rule to create/update/delete, "
            + "which table, which fields, which conditions, which actions")] string automationSpec = "",
        CancellationToken cancellationToken = default,
        [Description("ALIAS for `automationSpec`. Optional. Used only when `automationSpec` is empty.")] string reason = "",
        [Description("ALIAS for `automationSpec`. Optional. Used only when `automationSpec` and `reason` are empty.")] string summary = "")
    {
        var effective = HandoffTool.PickEffectiveSpec(automationSpec, reason, summary);
        return await HandoffTool.ExecuteHandoffAsync(projectStateStore, sessionContext, AgentNames.AutomationBuilder, "Automation Builder", effective, cancellationToken);
    }
}
