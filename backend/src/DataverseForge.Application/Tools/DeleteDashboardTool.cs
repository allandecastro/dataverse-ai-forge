using System.ComponentModel;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class DeleteDashboardTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<DeleteDashboardTool> logger)
{
    [Description("Delete an org-owned Dataverse dashboard by dashboardId. DESTRUCTIVE — "
        + "request_approval BEFORE calling. Use get_dashboards_list to find the dashboardId. "
        + "Managed dashboards cannot be deleted; Dataverse rejects with HTTP 403.")]
    public async Task<string> DeleteDashboardAsync(
        [Description("The GUID of the dashboard to delete (formid from get_dashboards_list)")] string dashboardId,
        [Description("Display name (for audit logging)")] string dashboardName,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(dashboardId, out var parsedId) || parsedId == Guid.Empty)
        {
            return "ERROR: dashboardId must be a valid GUID — call get_dashboards_list to retrieve it.";
        }

        try
        {
            await dataverseClient.DeleteDashboardAsync(parsedId, cancellationToken);

            // F-23: re-read state right before mutate+save.
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.Dashboards.RemoveAll(d => d.DashboardId == parsedId);
                fresh.OperationStack.Push(new ExecutedOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    ExecutedAt = DateTime.UtcNow,
                    AgentName = AgentNames.UIBuilder,
                    ToolName = "DeleteDashboard",
                    Parameters = new { dashboardId = parsedId, dashboardName },
                    Result = new { dashboardId = parsedId },
                    IsReversible = false,
                    ReverseOp = null
                });
                fresh.AuditLog.Add(AuditEntry.Ok(AgentNames.UIBuilder, "DeleteDashboard", $"Deleted dashboard '{dashboardName}' (id={parsedId})"));
            }, cancellationToken);
            return $"Dashboard '{dashboardName}' (id={parsedId}) deleted.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "DeleteDashboard failed: id={Id}, name={Name}", parsedId, dashboardName);
            return $"ERROR: Failed to delete dashboard '{dashboardName}' (id={parsedId}): {ex.Message}. "
                + "Common cause: the dashboard is part of a managed solution.";
        }
    }
}
