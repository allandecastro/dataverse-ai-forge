using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class GetTablesListTool(
    IDataverseClient dataverseClient,
    ILogger<GetTablesListTool> logger)
{
    [Description("Get list of Dataverse tables (entities) in the environment. Filter by 'all', 'custom', 'oob', or 'solution'. When filter is 'solution', solutionName is required. Optional searchTerm filters by logical name or display name.")]
    public async Task<string> GetTablesListAsync(
        [Description("Filter type: 'all' (default), 'custom', 'oob', or 'solution'")] string filter,
        [Description("Solution unique name (required when filter is 'solution'). Pass empty string when not used.")] string solutionName,
        [Description("Search term to filter by logical name or display name. Pass empty string when not used.")] string searchTerm,
        CancellationToken cancellationToken)
    {
        try
        {
            var sn = string.IsNullOrWhiteSpace(solutionName) ? null : solutionName;
            var st = string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm;
            var tables = await dataverseClient.GetTablesListAsync(filter, sn, st, cancellationToken);
            return System.Text.Json.JsonSerializer.Serialize(tables);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetTablesList failed (filter={Filter}, solutionName={SolutionName})", filter, solutionName);
            return $"ERROR: Could not retrieve tables list. {ex.Message}";
        }
    }
}
