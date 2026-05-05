using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetDashboardsListTool(IDataverseClient dataverseClient)
{
    [Description("List org-owned dashboards (systemforms with type=0) in the environment. Read-only. "
        + "Returns id, name, description, isDefault, isManaged for each. Use this before "
        + "plan_update_dashboard or delete_dashboard to find the dashboardId.")]
    public async Task<IReadOnlyList<DashboardInfo>> GetDashboardsListAsync(CancellationToken cancellationToken)
        => await dataverseClient.GetDashboardsListAsync(cancellationToken);
}
