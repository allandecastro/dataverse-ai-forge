using System.ComponentModel;
using DataverseForge.Domain.Interfaces;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Read-only fetch of actual record rows from a Dataverse table. Closes a real
/// gap that surfaced in DataPopulation: with only <c>CountRecordsTool</c> the
/// agent could not retrieve target GUIDs for lookup fields, so it would loop
/// asking the user to paste them. This tool makes that flow self-service.
/// </summary>
public class GetRecordsTool(IDataverseClient dataverseClient)
{
    [Description("Fetch up to 50 records from a Dataverse table — read-only, returns the actual rows. "
        + "Use this when DataPopulation needs real GUIDs for lookup fields (e.g. fetch a Contact "
        + "before creating a Gift that references one), or when the Tester wants to spot-check "
        + "row content (not just count). Pair with $select to keep payloads small. Read-only — "
        + "does not modify any data. On failure returns { error, entitySetName } so the agent can "
        + "quote the real error verbatim instead of saying 'function failed'.")]
    public async Task<object> GetRecordsAsync(
        [Description("Entity set name (plural) — e.g. 'contacts', 'eppc_gifts', 'accounts'")] string entitySetName,
        // Defaults are explicit so AIFunctionFactory treats these as optional and
        // tolerates the agent omitting them. Without defaults, calls like
        // GetRecords(entitySetName, top) — which happen any time the agent
        // doesn't need filtering — fail at the binder with the opaque
        // "Error: Function failed." string before the try/catch below ever runs.
        [Description("Optional comma-separated $select list — e.g. 'contactid,fullname'. Omit to fetch all columns.")] string? select = null,
        [Description("Optional OData $filter — e.g. \"contains(fullname,'Smith')\"")] string? filter = null,
        [Description("Optional $orderby — e.g. 'modifiedon desc'")] string? orderby = null,
        [Description("Max rows to return (1-50, default 5)")] int top = 5,
        CancellationToken cancellationToken = default)
    {
        if (top <= 0) top = 5;
        if (top > 50) top = 50;

        // Catch and surface real errors. Without this, AIFunctionFactory turns
        // any exception into a generic "Function failed." string and the agent
        // tells the user "function failed" instead of the actual Dataverse error.
        try
        {
            return await dataverseClient.GetRecordsAsync(entitySetName, select, filter, orderby, top, cancellationToken);
        }
        catch (Exception ex)
        {
            return new
            {
                error = $"GetRecords failed for '{entitySetName}': {ex.Message}",
                entitySetName,
                select,
                filter,
                orderby,
                top,
            };
        }
    }
}
