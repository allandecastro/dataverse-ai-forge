using System.ComponentModel;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteUpdateDashboardTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteUpdateDashboardTool> logger)
{
    [Description("Execute an UPDATE to an existing org-owned Dataverse dashboard. Pass the planId "
        + "from plan_update_dashboard.")]
    public async Task<string> ExecuteUpdateDashboardAsync(
        [Description("planId returned by plan_update_dashboard")] string planId,
        [Description("The GUID of the dashboard being updated (must match the plan)")] string dashboardId,
        [Description("Display name (must match the plan)")] string dashboardName,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(dashboardId, out var parsedId) || parsedId == Guid.Empty)
        {
            return $"ERROR: dashboardId '{dashboardId}' is not a valid GUID. Use get_dashboards_list to find it.";
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (planId.StartsWith("rejected_dashboard_invalid_", StringComparison.Ordinal))
        {
            return $"SKIPPED: Plan '{planId}' was rejected. Fix and re-plan.";
        }

        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            return $"ALREADY_EXECUTED: Plan '{planId}' already executed earlier this session.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            return $"ERROR: Plan '{planId}' not found. Call plan_update_dashboard first.";
        }

        try
        {
            await dataverseClient.UpdateDashboardAsync(parsedId, webApiPayloadJson, cancellationToken);

            try
            {
                await dataverseClient.PublishAllAsync(cancellationToken);
            }
            catch (Exception pubEx)
            {
                logger.LogWarning(pubEx, "PublishAll after dashboard update failed");
            }

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
                    ToolName = "ExecuteUpdateDashboard",
                    Parameters = new { dashboardId = parsedId, dashboardName },
                    Result = new { dashboardId = parsedId },
                    IsReversible = false,
                    ReverseOp = null
                });
                fresh.AuditLog.Add(new AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    Action = "ExecuteUpdateDashboard",
                    Detail = $"Updated dashboard '{dashboardName}' ({parsedId})",
                    Success = true
                });
            }, cancellationToken);
            return $"Dashboard '{dashboardName}' (id={parsedId}) updated.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteUpdateDashboard failed: dashboardId={Id}, name={Name}", parsedId, dashboardName);
            return $"ERROR: Failed to update dashboard '{dashboardName}' (id={parsedId}): {ex.Message}";
        }
    }
}
