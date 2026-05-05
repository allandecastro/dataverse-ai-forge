using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetDashboardDefinitionTool(IDataverseClient dataverseClient)
{
    [Description("Read a dashboard's full FormXML for inspection / pre-update editing. Read-only. "
        + "Pass the dashboardId from get_dashboards_list. Useful when planning an update to an "
        + "existing dashboard — read the current shape first, then plan the modified version.")]
    public async Task<DashboardDefinition> GetDashboardDefinitionAsync(
        [Description("The dashboard GUID (formid)")] string dashboardId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(dashboardId, out var parsed) || parsed == Guid.Empty)
        {
            throw new ArgumentException(
                $"dashboardId '{dashboardId}' is not a valid GUID. Use get_dashboards_list to find it.",
                nameof(dashboardId));
        }
        return await dataverseClient.GetDashboardDefinitionAsync(parsed, cancellationToken);
    }
}
