using System.ComponentModel;
using DataverseForge.Domain.Interfaces;

namespace DataverseForge.Application.Tools;

public class CountRecordsTool(IDataverseClient dataverseClient)
{
    [Description("Get the count of records in a Dataverse table. "
        + "Optionally filter by an OData filter expression. "
        + "Read-only operation — does not modify any data. "
        + "On failure returns { error, entitySetName, filter } so the agent can quote "
        + "the real error verbatim instead of the generic 'function failed'.")]
    public async Task<object> CountRecordsAsync(
        [Description("The entity set name for the OData endpoint (e.g., 'cr123_feedbackcategories')")] string entitySetName,
        // Default makes filter optional at the binder so calls without it bind
        // cleanly (otherwise the framework rejects with "Error: Function failed.").
        [Description("Optional OData $filter expression (e.g., \"contains(cr123_name,'test')\")")] string? filter = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var count = await dataverseClient.GetRecordCountAsync(entitySetName, filter, cancellationToken);
            return new { count };
        }
        catch (Exception ex)
        {
            return new
            {
                error = $"CountRecords failed for '{entitySetName}': {ex.Message}",
                entitySetName,
                filter,
            };
        }
    }
}
