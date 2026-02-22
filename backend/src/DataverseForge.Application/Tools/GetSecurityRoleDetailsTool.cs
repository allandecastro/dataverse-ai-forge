using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetSecurityRoleDetailsTool(IDataverseClient dataverseClient)
{
    [Description("Get the full privilege details of a specific security role, including all entity-level privileges and their depths.")]
    public async Task<SecurityRoleDetails> GetSecurityRoleDetailsAsync(
        [Description("The GUID of the security role to inspect")] Guid roleId,
        CancellationToken cancellationToken)
    {
        return await dataverseClient.GetSecurityRoleDetailsAsync(roleId, cancellationToken);
    }
}
