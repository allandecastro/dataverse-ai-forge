using DataverseForge.Application.Agents;
using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;

namespace DataverseForge.Application.Tools;

public class HandoffToSecurityBuilderTool(IProjectStateStore projectStateStore, ISessionContext sessionContext)
{
    [Description("Hand off the conversation to the Security Builder agent for security-role "
        + "lifecycle work — create / update / delete a Dataverse security role, with privilege "
        + "management (Create / Read / Write / Delete / Append / AppendTo / Assign / Share at "
        + "User / BU / ParentChild / Org depth). Use this when the user asks for role creation, "
        + "privilege grants, or role removal. Field-level security profiles + column security "
        + "are NOT supported — refuse those with a maker-portal pointer instead of handing off.")]
    public async Task<string> HandoffToSecurityBuilderAsync(
        [Description("The role specification — what role to create / update / delete, which BU, "
            + "which tables + access rights + depth")] string roleSpec = "",
        CancellationToken cancellationToken = default,
        [Description("ALIAS for `roleSpec`. Optional. Used only when `roleSpec` is empty.")] string reason = "",
        [Description("ALIAS for `roleSpec`. Optional. Used only when `roleSpec` and `reason` are empty.")] string summary = "")
    {
        var effective = HandoffTool.PickEffectiveSpec(roleSpec, reason, summary);
        return await HandoffTool.ExecuteHandoffAsync(projectStateStore, sessionContext, AgentNames.SecurityBuilder, "Security Builder", effective, cancellationToken);
    }
}
