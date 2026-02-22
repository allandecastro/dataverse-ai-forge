using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetFormsListTool(IDataverseClient dataverseClient)
{
    [Description("Get list of forms for a Dataverse table. Returns form ID, name, type (main, quickCreate, quickView), and whether it is the default form.")]
    public async Task<IReadOnlyList<FormInfo>> GetFormsListAsync(
        [Description("The logical name of the table (e.g., 'cr123_workorder')")] string tableLogicalName,
        CancellationToken cancellationToken)
    {
        return await dataverseClient.GetFormsListAsync(tableLogicalName, cancellationToken);
    }
}
