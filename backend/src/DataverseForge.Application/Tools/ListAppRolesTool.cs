using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ListAppRolesTool(IDataverseClient dataverseClient)
{
    [Description("List the security roles assigned to a model-driven app via the "
        + "appmoduleroles_association M:N. Read-only. Returns roleId, name, businessUnitId, "
        + "isManaged for each. Empty list = no roles assigned (only System Administrator can "
        + "see the app, by default). Use this before assigning / unassigning roles in later phases.")]
    public async Task<IReadOnlyList<AppModuleRoleInfo>> ListAppRolesAsync(
        [Description("The appmoduleid (GUID) of the app whose roles should be listed.")] Guid appId,
        CancellationToken cancellationToken)
    {
        return await dataverseClient.ListAppRolesAsync(appId, cancellationToken);
    }
}
