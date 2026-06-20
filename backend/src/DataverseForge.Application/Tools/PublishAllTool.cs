using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Publishes all pending customizations across the active environment via
/// Dataverse's <c>PublishAllXml</c> action. Required so newly-created /
/// edited components (table fields, forms, views, option sets) become visible
/// to model-driven apps, downstream consumers, and most importantly are
/// included when a solution is exported.
/// </summary>
public class PublishAllTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<PublishAllTool> logger)
{
    [Description("Publish all pending customizations in the environment via the PublishAllXml action. "
        + "Use this BEFORE exporting a solution so the exported zip includes the latest schema/UI "
        + "changes — unpublished forms/views are NOT exported. Also run after a batch of "
        + "creates/updates if downstream consumers need to see the changes immediately. "
        + "Can take 30s–several minutes on large environments. Call request_approval first — "
        + "publish is global and visible to other users.")]
    public async Task<string> PublishAllAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("PublishAll executing");

        try
        {
            await dataverseClient.PublishAllAsync(cancellationToken);

            // F-23: re-read state right before mutate+save so concurrent writes
            // landed during the multi-minute publish above survive.
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
                fresh.AuditLog.Add(AuditEntry.Ok(AgentNames.SolutionArchitect, "PublishAll", "Published all customizations across the environment")),
                cancellationToken);

            sw.Stop();
            ForgeActivitySource.RecordToolDuration("PublishAll", "success", sw.ElapsedMilliseconds);
            return $"Published all customizations ({sw.ElapsedMilliseconds:N0} ms). Newly-created and "
                + "updated components are now visible to consumers and ready to be included in a "
                + "solution export.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "PublishAll failed");
            var failureDetail = ex.Message;
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
                fresh.AuditLog.Add(AuditEntry.Failed(AgentNames.SolutionArchitect, "PublishAll", $"PublishAll failed: {failureDetail}")),
                cancellationToken);
            sw.Stop();
            ForgeActivitySource.RecordToolDuration("PublishAll", "error", sw.ElapsedMilliseconds);
            return $"ERROR: PublishAllXml failed: {ex.Message}. Common causes: long-running publish "
                + "exceeded the timeout, or the user account lacks PublishCustomizations privilege.";
        }
    }
}
