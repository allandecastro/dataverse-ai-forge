using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Reads a single security role's details — adds parent role + customizable
/// flag on top of the list shape. Read-only.
/// </summary>
public class GetSecurityRoleDetailsTool(IDataverseClient dataverseClient)
{
    [Description("Read details of a single security role by its roleid (GUID). Returns the role's "
        + "name, description, business unit, parent role, customizable flag, etc. Read-only. "
        + "Useful when the agent already has the role id (from get_security_roles) and wants "
        + "to confirm details before assigning it to an app.")]
    public async Task<SecurityRoleDetails?> GetSecurityRoleDetailsAsync(
        [Description("roleid (GUID).")] string roleId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(roleId, out var parsed) || parsed == Guid.Empty)
        {
            return null;
        }
        return await dataverseClient.GetSecurityRoleAsync(parsed, cancellationToken);
    }
}
