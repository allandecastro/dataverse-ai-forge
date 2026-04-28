using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class GetSolutionsListTool(
    IDataverseClient dataverseClient,
    ILogger<GetSolutionsListTool> logger)
{
    [Description("Get list of unmanaged Dataverse solutions in the environment, including solution ID, unique name, display name, version, publisher, and component count.")]
    public async Task<string> GetSolutionsListAsync(CancellationToken cancellationToken)
    {
        try
        {
            var solutions = await dataverseClient.GetSolutionsListAsync(cancellationToken);
            return System.Text.Json.JsonSerializer.Serialize(solutions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetSolutionsList failed");
            return $"ERROR: Could not retrieve solutions. {ex.Message}";
        }
    }
}
