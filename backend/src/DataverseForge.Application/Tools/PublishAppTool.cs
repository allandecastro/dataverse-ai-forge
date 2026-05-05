using System.ComponentModel;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class PublishAppTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<PublishAppTool> logger)
{
    [Description("Publish a single model-driven app via the PublishXml action with a scoped "
        + "<appmodule> envelope. Cheaper than PublishAll for one app. Required after sitemap / "
        + "component / metadata edits for the new content to be visible to consumers.")]
    public async Task<string> PublishAppAsync(
        [Description("appmoduleid (GUID) — get from get_apps_list.")] string appId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(appId, out var parsedId) || parsedId == Guid.Empty)
        {
            return "ERROR: appId must be a valid GUID — call get_apps_list to retrieve it.";
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            await dataverseClient.PublishAppAsync(parsedId, cancellationToken);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "PublishApp",
                Detail = $"Published app id={parsedId}",
                Success = true
            });
            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
            return $"App id={parsedId} published.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "PublishApp failed: appId={AppId}", parsedId);
            return $"ERROR: Failed to publish app id={parsedId}: {ex.Message}. "
                + "Common cause: the user account lacks PublishCustomizations privilege.";
        }
    }
}
