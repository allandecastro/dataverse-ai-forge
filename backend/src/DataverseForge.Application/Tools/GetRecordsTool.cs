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
        + "does not modify any data.")]
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRecordsAsync(
        [Description("Entity set name (plural) — e.g. 'contacts', 'eppc_gifts', 'accounts'")] string entitySetName,
        [Description("Optional comma-separated $select list — e.g. 'contactid,fullname'. Omit to fetch all columns.")] string? select,
        [Description("Optional OData $filter — e.g. \"contains(fullname,'Smith')\"")] string? filter,
        [Description("Optional $orderby — e.g. 'modifiedon desc'")] string? orderby,
        [Description("Max rows to return (1-50, default 5)")] int top,
        CancellationToken cancellationToken)
    {
        if (top <= 0) top = 5;
        if (top > 50) top = 50;
        return await dataverseClient.GetRecordsAsync(entitySetName, select, filter, orderby, top, cancellationToken);
    }
}
