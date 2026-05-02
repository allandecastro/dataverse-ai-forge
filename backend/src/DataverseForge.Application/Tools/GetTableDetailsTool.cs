using System.ComponentModel;
using DataverseForge.Domain.Interfaces;

namespace DataverseForge.Application.Tools;

public class GetTableDetailsTool(IDataverseClient dataverseClient)
{
    [Description("Get detailed metadata for a specific Dataverse table including columns, relationships, and alternate keys. "
        + "Returns the TableDetails on success or an object with an `error` string describing the failure.")]
    public async Task<object> GetTableDetailsAsync(
        [Description("The logical name of the table (e.g., 'account', 'contact')")] string tableLogicalName,
        CancellationToken cancellationToken)
    {
        // Catch and reformat exceptions so agents see the real error instead of the
        // generic "Function failed." string AIFunctionFactory produces on uncaught throws.
        try
        {
            return await dataverseClient.GetTableDetailsAsync(tableLogicalName, cancellationToken);
        }
        catch (Exception ex)
        {
            return new
            {
                error = $"GetTableDetails failed for '{tableLogicalName}': {ex.Message}",
                tableLogicalName,
            };
        }
    }
}
