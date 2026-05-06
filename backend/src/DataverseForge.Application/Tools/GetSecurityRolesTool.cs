using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Lists every security role in the environment, including BOTH managed
/// (System Administrator, Sales Person, etc.) and unmanaged custom roles.
/// Read-only. Used by UIBuilder to look up a role's id before
/// <c>assign_app_role</c>, by SolutionArchitect to answer "what roles
/// exist?" questions directly, and (later) by Security Builder.
/// </summary>
public class GetSecurityRolesTool(IDataverseClient dataverseClient)
{
    [Description("List security roles in the Dataverse environment. Returns roleid, name, "
        + "description, business unit, IsManaged, IsAutoAssigned, AppliesTo. Includes BOTH "
        + "managed and unmanaged roles — most useful OOB roles (System Administrator, Sales "
        + "Person, etc.) are managed. Optional searchTerm does a case-insensitive "
        + "name-contains match. Optional businessUnitId filters to a single BU. Read-only.")]
    public async Task<IReadOnlyList<SecurityRoleInfo>> GetSecurityRolesAsync(
        [Description("Optional name search (case-insensitive contains). Leave empty/null for all roles.")]
        string? searchTerm,
        [Description("Optional business unit ID (GUID) to filter by. Leave empty/null for all BUs.")]
        string? businessUnitId,
        CancellationToken cancellationToken)
    {
        Guid? buGuid = null;
        if (!string.IsNullOrWhiteSpace(businessUnitId))
        {
            if (Guid.TryParse(businessUnitId, out var parsed) && parsed != Guid.Empty)
            {
                buGuid = parsed;
            }
            // Otherwise silently ignore — the agent sometimes passes the BU
            // name by mistake; better to surface "no roles with that filter"
            // than block the call.
        }
        return await dataverseClient.GetSecurityRolesAsync(searchTerm, buGuid, cancellationToken);
    }
}
