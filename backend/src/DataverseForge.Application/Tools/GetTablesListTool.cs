using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class GetTablesListTool(
    IDataverseClient dataverseClient,
    ILogger<GetTablesListTool> logger)
{
    /// <summary>
    /// Hard cap on rows to return. A typical Dataverse env has 5000+ tables; returning all
    /// of them eats the model's context budget and produces nonsense responses (we hit this
    /// when SA called GetTablesList(filter:"all", searchTerm:"") looking for "Account" and
    /// got drowned in OOB metadata). When the result would exceed this cap, we return a
    /// truncated list + a hint to use a searchTerm.
    /// </summary>
    private const int MaxRows = 50;

    [Description("Get list of Dataverse tables (entities). Capped at 50 rows — provide a "
        + "searchTerm to narrow results when looking for a specific table. "
        + "Filter: 'all', 'custom', 'oob', or 'solution' (solutionName required for 'solution'). "
        + "If you know the table's logical name (e.g. 'account', 'contact'), pass it as searchTerm "
        + "instead of fetching all and scanning. ALWAYS pass a non-empty searchTerm when looking "
        + "for a known OOB table — fetching all tables wastes context and rarely finds what you need.")]
    public async Task<string> GetTablesListAsync(
        [Description("Filter type: 'all', 'custom', 'oob', or 'solution'")] string filter,
        [Description("Solution unique name (required when filter is 'solution'). Pass empty string when not used.")] string solutionName,
        [Description("Search term — substring match on logical name / schema name. STRONGLY recommended for filter='all'. Pass empty string only when listing a small known set (e.g. a specific solution).")] string searchTerm,
        CancellationToken cancellationToken)
    {
        try
        {
            var sn = string.IsNullOrWhiteSpace(solutionName) ? null : solutionName;
            var st = string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm;
            var tables = await dataverseClient.GetTablesListAsync(filter, sn, st, cancellationToken);

            if (tables.Count > MaxRows)
            {
                logger.LogInformation("GetTablesList: truncating {Count} rows to {Cap} (filter={Filter}, searchTerm={SearchTerm})",
                    tables.Count, MaxRows, filter, searchTerm);
                var truncated = tables.Take(MaxRows).ToList();
                return System.Text.Json.JsonSerializer.Serialize(new
                {
                    truncated = true,
                    totalCount = tables.Count,
                    returnedCount = truncated.Count,
                    hint = $"Result truncated to {MaxRows} rows. Pass a more specific searchTerm to narrow the list (e.g. searchTerm=\"account\" for the Account table).",
                    tables = truncated,
                });
            }

            return System.Text.Json.JsonSerializer.Serialize(tables);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetTablesList failed (filter={Filter}, solutionName={SolutionName})", filter, solutionName);
            return $"ERROR: Could not retrieve tables list. {ex.Message}";
        }
    }
}
