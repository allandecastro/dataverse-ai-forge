using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Runs an existing system view's FetchXML and returns the rows. The big DRY
/// win: the Tester can verify "after creating 10 records, does the Active
/// Gifts view show all 10?" by running the view's own FetchXML — no inventing
/// a new query that drifts from the view definition over time.
/// </summary>
public class RunSavedQueryTool(IDataverseClient dataverseClient)
{
    [Description("Run an existing system view (savedquery) and return its rows. Read-only. "
        + "Resolves the view's FetchXML and entity set automatically — pass only the savedQueryId. "
        + "Use this when verifying a view actually returns the expected rows (Tester), or when an "
        + "agent wants to see what's in a view without re-authoring its query (SA, DataPopulation). "
        + "Returns an envelope { savedQueryId, viewName, entityLogicalName, entitySetName, fetchXml, "
        + "rowCount, rows[] } so the agent can confirm exactly which view ran and against what.")]
    public async Task<object> RunSavedQueryAsync(
        [Description("The savedquery GUID — find it via get_views_list.")] string savedQueryId,
        // Default makes top optional at the binder.
        [Description("Max rows to return (1-500, default 50). Caps the result.")] int top = 50,
        CancellationToken cancellationToken = default)
    {
        // Catch and surface real errors so the agent can quote the actual cause
        // (e.g. "view not found", auth issue, malformed FetchXML in the view)
        // instead of the framework's generic "function failed".
        try
        {
            if (!Guid.TryParse(savedQueryId, out var parsed) || parsed == Guid.Empty)
            {
                throw new ArgumentException(
                    $"savedQueryId '{savedQueryId}' is not a valid GUID. Use get_views_list to find one.",
                    nameof(savedQueryId));
            }
            if (top <= 0) top = 50;
            if (top > 500) top = 500;

            return await dataverseClient.RunSavedQueryAsync(parsed, top, cancellationToken);
        }
        catch (Exception ex)
        {
            return new
            {
                error = $"RunSavedQuery failed for '{savedQueryId}': {ex.Message}",
                savedQueryId,
                top,
            };
        }
    }
}
