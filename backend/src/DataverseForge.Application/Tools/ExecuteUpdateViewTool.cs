using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteUpdateViewTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteUpdateViewTool> logger)
{
    [Description("Execute an update to an existing Dataverse view. Pass the planId from plan_update_view — "
        + "the OData JSON payload was stored server-side and will be loaded automatically. Do NOT try to "
        + "construct or pass the payload yourself.")]
    public async Task<string> ExecuteUpdateViewAsync(
        [Description("planId returned by plan_update_view — must match exactly. Payload is stored server-side; "
            + "do NOT construct the OData JSON yourself.")] string planId,
        [Description("The GUID of the view being updated (from get_views_list)")] string viewId,
        [Description("The logical name of the table the view belongs to")] string tableLogicalName,
        [Description("The display name of the view (used for logging/audit)")] string viewName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteUpdateView executing: planId={PlanId}, viewId={ViewId}, tableLogicalName={TableLogicalName}",
            planId, viewId, tableLogicalName);

        if (!Guid.TryParse(viewId, out var parsedViewId) || parsedViewId == Guid.Empty)
        {
            return "ERROR: viewId must be a valid GUID — call get_views_list to retrieve it.";
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (planId.StartsWith("rejected_view_invalid_", StringComparison.Ordinal))
        {
            return $"SKIPPED: Plan '{planId}' was rejected by plan_update_view. "
                + "Fix the input and re-plan; do NOT execute this planId.";
        }

        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            logger.LogInformation("ExecuteUpdateView: planId {PlanId} already executed earlier this session", planId);
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier this session. "
                + $"View '{viewName}' (viewId={parsedViewId}) is already updated. DO NOT retry.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            logger.LogWarning("ExecuteUpdateView: planId not found in PendingPlans: {PlanId}", planId);
            return $"ERROR: Plan '{planId}' not found. Call plan_update_view first and pass its returned planId verbatim.";
        }

        try
        {
            await dataverseClient.UpdateViewAsync(parsedViewId, webApiPayloadJson, cancellationToken);

            // View updates are not pushed to the Views list (it tracks creates).
            // Reverse op: not trivially undoable — would require fetchxml/layoutxml
            // snapshot before update. Mark as non-reversible.
            //
            // F-23: re-read state right before mutate+save.
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.ExecutedPlans[planId] = webApiPayloadJson;
                fresh.PendingPlans.Remove(planId);
                fresh.OperationStack.Push(new ExecutedOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    ExecutedAt = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    ToolName = "ExecuteUpdateView",
                    Parameters = new { viewId, tableLogicalName, viewName },
                    Result = new { viewId },
                    IsReversible = false,
                    ReverseOp = null
                });
                fresh.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    Action = "ExecuteUpdateView",
                    Detail = $"Updated view '{viewName}' (viewId={parsedViewId}) on table '{tableLogicalName}'",
                    Success = true
                });
            }, cancellationToken);

            logger.LogInformation("ExecuteUpdateView succeeded: viewId={ViewId}, viewName={ViewName}", parsedViewId, viewName);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteUpdateView"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"View '{viewName}' (viewId={parsedViewId}) updated on table '{tableLogicalName}'. "
                + "NEXT STEP: call handoff_to_tester now to verify the view in Dataverse — do NOT reply to the user yet.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteUpdateView failed: viewId={ViewId}, viewName={ViewName}", parsedViewId, viewName);

            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    Action = "ExecuteUpdateView",
                    Detail = $"Failed to update view '{viewName}' (viewId={parsedViewId}) on table '{tableLogicalName}': {ex.Message}",
                    Success = false
                });
            }, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteUpdateView"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to update view '{viewName}' (viewId={parsedViewId}) on table '{tableLogicalName}': {ex.Message}. "
                + "Check that the view exists, the payload is valid, and you have permission. "
                + "You may retry or hand off to Solution Architect for diagnosis.";
        }
    }
}
