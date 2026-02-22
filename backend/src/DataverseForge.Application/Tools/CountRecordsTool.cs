using System.ComponentModel;
using DataverseForge.Domain.Interfaces;

namespace DataverseForge.Application.Tools;

public class CountRecordsTool(IDataverseClient dataverseClient)
{
    [Description("Get the count of records in a Dataverse table. "
        + "Optionally filter by an OData filter expression. "
        + "Read-only operation — does not modify any data.")]
    public async Task<int> CountRecordsAsync(
        [Description("The entity set name for the OData endpoint (e.g., 'cr123_feedbackcategories')")] string entitySetName,
        [Description("Optional OData $filter expression (e.g., \"contains(cr123_name,'test')\")")] string? filter,
        CancellationToken cancellationToken)
    {
        return await dataverseClient.GetRecordCountAsync(entitySetName, filter, cancellationToken);
    }
}
