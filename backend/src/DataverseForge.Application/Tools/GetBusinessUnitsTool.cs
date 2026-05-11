using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetBusinessUnitsTool(IDataverseClient dataverseClient)
{
    [Description("List the business units in the Dataverse environment. Every security role is "
        + "scoped to one business unit — call this BEFORE plan_create_security_role when the user "
        + "didn't specify a BU. The first row in most environments is the root BU (matches the "
        + "organization name); custom roles usually bind to it unless you're in a multi-org tenant.")]
    public async Task<IReadOnlyList<BusinessUnitInfo>> GetBusinessUnitsAsync(
        CancellationToken cancellationToken = default)
    {
        return await dataverseClient.GetBusinessUnitsAsync(cancellationToken);
    }
}
