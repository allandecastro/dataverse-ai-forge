using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetRolePrivilegesTool(IDataverseClient dataverseClient)
{
    [Description("Read the privileges currently attached to a security role — privilege id + "
        + "depth (Basic / Local / Deep / Global). Used by plan_update_security_role to compute "
        + "the privilege delta against a target spec, and by the Tester for verification. "
        + "Call get_security_role_details for the role's metadata (name / IsManaged / etc.) — "
        + "this tool only returns the privilege matrix.")]
    public async Task<IReadOnlyList<RolePrivilegeAssignment>> GetRolePrivilegesAsync(
        [Description("RoleId of the security role (from get_security_roles)")] Guid roleId,
        CancellationToken cancellationToken = default)
    {
        return await dataverseClient.GetRolePrivilegesAsync(roleId, cancellationToken);
    }
}
