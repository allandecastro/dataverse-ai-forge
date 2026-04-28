using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetEnvironmentInfoTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Get Dataverse environment information including organization name, URL, base language, version, region, currency, and audit status. Persists env info to ProjectState so the sidebar reflects it.")]
    public async Task<EnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken)
    {
        var info = await dataverseClient.GetEnvironmentInfoAsync(cancellationToken);

        // Persist to ProjectState so the sidebar STATE_DELTA picks it up
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.EnvironmentName = info.OrganizationName;
        state.EnvironmentUrl = info.EnvironmentUrl;
        state.LastActivityAt = DateTime.UtcNow;
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return info;
    }
}
