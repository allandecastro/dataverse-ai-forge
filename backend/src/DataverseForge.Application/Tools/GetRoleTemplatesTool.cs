using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetRoleTemplatesTool(IDataverseClient dataverseClient)
{
    [Description("List Dataverse role templates. Templates mark a role so the platform "
        + "auto-replicates it into every new business unit as the unit is created — useful for "
        + "multi-org tenants where every BU should have a baseline of identical roles. Pass the "
        + "RoleTemplateId returned here to plan_create_security_role's optional roleTemplateId "
        + "argument to bind a new role to the template at create time. Never invent a template "
        + "GUID — every value passed to the planner must come verbatim from this tool's output.")]
    public async Task<IReadOnlyList<RoleTemplateInfo>> GetRoleTemplatesAsync(
        CancellationToken cancellationToken = default)
    {
        return await dataverseClient.GetRoleTemplatesAsync(cancellationToken);
    }
}
