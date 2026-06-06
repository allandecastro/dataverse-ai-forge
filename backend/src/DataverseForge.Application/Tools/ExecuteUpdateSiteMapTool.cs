using System.ComponentModel;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteUpdateSiteMapTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteUpdateSiteMapTool> logger)
{
    [Description("Execute an update to a Dataverse sitemap. Pass the planId from plan_update_sitemap. "
        + "Uses If-Match: * so a missing sitemap is rejected cleanly.")]
    public async Task<string> ExecuteUpdateSiteMapAsync(
        [Description("planId returned by plan_update_sitemap.")] string planId,
        [Description("sitemapid (GUID) — must match the plan's siteMapId.")] string siteMapId,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("ExecuteUpdateSiteMap executing: planId={PlanId}, siteMapId={SiteMapId}", planId, siteMapId);

        if (!Guid.TryParse(siteMapId, out var parsedId) || parsedId == Guid.Empty)
        {
            return "ERROR: siteMapId must be a valid GUID.";
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (planId.StartsWith("rejected_update_sitemap_", StringComparison.Ordinal))
        {
            return $"SKIPPED: Plan '{planId}' was rejected by plan_update_sitemap. Fix the spec and re-plan.";
        }

        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier this session. DO NOT retry.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            return $"ERROR: Plan '{planId}' not found. Call plan_update_sitemap first and pass its planId verbatim.";
        }

        try
        {
            await dataverseClient.UpdateSiteMapAsync(parsedId, webApiPayloadJson, cancellationToken);

            // F-23: re-read state right before mutate+save.
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.ExecutedPlans[planId] = webApiPayloadJson;
                fresh.PendingPlans.Remove(planId);
                fresh.PendingPlanTimestamps.Remove(planId);
                fresh.OperationStack.Push(new ExecutedOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    ExecutedAt = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    ToolName = "ExecuteUpdateSiteMap",
                    Parameters = new { siteMapId = parsedId, planId },
                    Result = new { siteMapId = parsedId },
                    IsReversible = false,
                    ReverseOp = null,
                });
                fresh.AuditLog.Add(AuditEntry.Ok(AgentNames.UIBuilder, "ExecuteUpdateSiteMap", $"Updated sitemap id={parsedId}"));
            }, cancellationToken);
            return $"Sitemap id={parsedId} updated. Run publish_app on the attached app for the changes to be visible.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "ExecuteUpdateSiteMap failed: id={Id}", parsedId);
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(AuditEntry.Failed(AgentNames.UIBuilder, "ExecuteUpdateSiteMap", $"Failed to update sitemap id={parsedId}: {ex.Message}"));
            }, cancellationToken);
            return $"ERROR: Failed to update sitemap id={parsedId}: {ex.Message}.";
        }
    }
}
