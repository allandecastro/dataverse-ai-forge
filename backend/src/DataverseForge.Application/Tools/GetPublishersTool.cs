using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class GetPublishersTool(
    IDataverseClient dataverseClient,
    ILogger<GetPublishersTool> logger)
{
    [Description("Get list of non-readonly publishers in the Dataverse environment, including publisher ID, display name, unique name, prefix, and option value prefix.")]
    public async Task<string> GetPublishersAsync(CancellationToken cancellationToken)
    {
        try
        {
            var publishers = await dataverseClient.GetPublishersAsync(cancellationToken);
            return System.Text.Json.JsonSerializer.Serialize(publishers);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetPublishers failed");
            return $"ERROR: Could not retrieve publishers. {ex.Message}";
        }
    }
}
