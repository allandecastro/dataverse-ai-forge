using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetChartsListTool(IDataverseClient dataverseClient)
{
    [Description("List all charts (savedqueryvisualization rows) attached to a Dataverse table. "
        + "Read-only — returns chartId, name, description, primary entity, default flag, managed flag. "
        + "Call before plan_update_chart / delete_chart to find the right chartId.")]
    public async Task<IReadOnlyList<ChartInfo>> GetChartsListAsync(
        [Description("The logical name of the table (e.g., 'opportunity', 'eppc_gift')")] string tableLogicalName,
        CancellationToken cancellationToken)
    {
        return await dataverseClient.GetChartsListAsync(tableLogicalName, cancellationToken);
    }
}
