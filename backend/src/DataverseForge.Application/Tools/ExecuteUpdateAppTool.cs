using System.ComponentModel;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteUpdateAppTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteUpdateAppTool> logger)
{
    [Description("Execute an update to a model-driven app. Pass the planId from plan_update_app — "
        + "the OData JSON payload was stored server-side. Uses If-Match: * so a missing app is "
        + "rejected (instead of silently upserted). Publishes the app after success.")]
    public async Task<string> ExecuteUpdateAppAsync(
        [Description("planId returned by plan_update_app — must match exactly.")] string planId,
        [Description("appmoduleid (GUID) — must match the plan's appId.")] string appId,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("ExecuteUpdateApp executing: planId={PlanId}, appId={AppId}", planId, appId);

        if (!Guid.TryParse(appId, out var parsedId) || parsedId == Guid.Empty)
        {
            return "ERROR: appId must be a valid GUID — call get_apps_list to retrieve it.";
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (planId.StartsWith("rejected_update_app_", StringComparison.Ordinal))
        {
            return $"SKIPPED: Plan '{planId}' was rejected by plan_update_app. Fix the inputs and re-plan.";
        }

        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier this session. DO NOT retry.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            return $"ERROR: Plan '{planId}' not found. Call plan_update_app first and pass its planId verbatim.";
        }

        try
        {
            await dataverseClient.UpdateAppAsync(parsedId, webApiPayloadJson, cancellationToken);

            // Side-effect publish — does not touch ProjectState; stays outside apply block.
            var publishNote = string.Empty;
            try
            {
                await dataverseClient.PublishAppAsync(parsedId, cancellationToken);
                publishNote = " Published.";
            }
            catch (Exception pubEx) when (pubEx is not OperationCanceledException)
            {
                logger.LogWarning(pubEx, "PublishApp after update failed for {AppId}", parsedId);
                publishNote = $" (publish failed: {pubEx.Message} — admin can publish manually).";
            }

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
                    ToolName = "ExecuteUpdateApp",
                    Parameters = new { appId = parsedId, planId },
                    Result = new { appId = parsedId },
                    IsReversible = false,
                    ReverseOp = null
                });
                fresh.AuditLog.Add(AuditEntry.Ok(AgentNames.UIBuilder, "ExecuteUpdateApp", $"Updated app id={parsedId}"));
            }, cancellationToken);

            logger.LogInformation("ExecuteUpdateApp succeeded: appId={AppId}", parsedId);
            return $"App id={parsedId} updated.{publishNote}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "ExecuteUpdateApp failed: appId={AppId}", parsedId);
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(AuditEntry.Failed(AgentNames.UIBuilder, "ExecuteUpdateApp", $"Failed to update app id={parsedId}: {ex.Message}"));
            }, cancellationToken);
            return $"ERROR: Failed to update app id={parsedId}: {ex.Message}.";
        }
    }
}
