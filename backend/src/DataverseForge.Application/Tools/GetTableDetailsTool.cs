using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetTableDetailsTool(IDataverseClient dataverseClient)
{
    [Description("Get detailed metadata for a specific Dataverse table including columns, relationships, and alternate keys.")]
    public async Task<TableDetails> GetTableDetailsAsync(
        [Description("The logical name of the table (e.g., 'account', 'contact')")] string tableLogicalName,
        CancellationToken cancellationToken)
    {
        return await dataverseClient.GetTableDetailsAsync(tableLogicalName, cancellationToken);
    }
}
