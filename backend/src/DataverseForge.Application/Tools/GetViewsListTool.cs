using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetViewsListTool(IDataverseClient dataverseClient)
{
    [Description("Get list of system views (saved queries) for a Dataverse table. Returns view ID, name, and description.")]
    public async Task<IReadOnlyList<ViewInfo>> GetViewsListAsync(
        [Description("The logical name of the table (e.g., 'cr123_workorder')")] string tableLogicalName,
        CancellationToken cancellationToken)
    {
        return await dataverseClient.GetViewsListAsync(tableLogicalName, cancellationToken);
    }
}
