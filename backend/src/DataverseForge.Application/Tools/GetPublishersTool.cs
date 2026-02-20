using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetPublishersTool(IDataverseClient dataverseClient)
{
    [Description("Get list of non-readonly publishers in the Dataverse environment, including publisher ID, display name, unique name, prefix, and option value prefix.")]
    public async Task<IReadOnlyList<PublisherInfo>> GetPublishersAsync(CancellationToken cancellationToken)
    {
        return await dataverseClient.GetPublishersAsync(cancellationToken);
    }
}
