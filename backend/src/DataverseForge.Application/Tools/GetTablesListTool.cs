using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetTablesListTool(IDataverseClient dataverseClient)
{
    [Description("Get list of Dataverse tables (entities) in the environment. Filter by 'all', 'custom', 'oob', or 'solution'. When filter is 'solution', solutionName is required. Optional searchTerm filters by logical name or display name.")]
    public async Task<IReadOnlyList<TableInfo>> GetTablesListAsync(
        [Description("Filter type: 'all' (default), 'custom', 'oob', or 'solution'")] string filter,
        [Description("Solution unique name (required when filter is 'solution')")] string? solutionName,
        [Description("Optional search term to filter by logical name or display name")] string? searchTerm,
        CancellationToken cancellationToken)
    {
        return await dataverseClient.GetTablesListAsync(filter, solutionName, searchTerm, cancellationToken);
    }
}
