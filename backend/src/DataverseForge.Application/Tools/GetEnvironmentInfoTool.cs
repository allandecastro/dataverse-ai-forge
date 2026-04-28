using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class GetEnvironmentInfoTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<GetEnvironmentInfoTool> logger)
{
    [Description("Get Dataverse environment information including organization name, URL, base language, version, region, currency, and audit status. Persists env info to ProjectState so the sidebar reflects it.")]
    public async Task<string> GetEnvironmentInfoAsync(CancellationToken cancellationToken)
    {
        try
        {
            var info = await dataverseClient.GetEnvironmentInfoAsync(cancellationToken);

            // Persist to ProjectState so the sidebar STATE_DELTA picks it up
            var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
            state.EnvironmentName = info.OrganizationName;
            state.EnvironmentUrl = info.EnvironmentUrl;
            state.LastActivityAt = DateTime.UtcNow;
            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            return System.Text.Json.JsonSerializer.Serialize(info);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetEnvironmentInfo failed");
            return $"ERROR: Could not retrieve environment info. {ex.Message}";
        }
    }
}
