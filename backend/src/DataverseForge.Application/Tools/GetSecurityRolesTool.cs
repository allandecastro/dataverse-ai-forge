using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetSecurityRolesTool(IDataverseClient dataverseClient)
{
    [Description("Get a list of security roles from the Dataverse environment. "
        + "Use filter to narrow results: 'all' (default), 'custom' (unmanaged only), or 'system' (managed only).")]
    public async Task<IReadOnlyList<SecurityRoleInfo>> GetSecurityRolesAsync(
        [Description("Filter: 'all', 'custom', or 'system'")] string filter,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filter))
            filter = "all";

        return await dataverseClient.GetSecurityRolesAsync(filter, cancellationToken);
    }
}
