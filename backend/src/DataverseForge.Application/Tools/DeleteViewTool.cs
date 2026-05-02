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
/// Direct delete of a Dataverse system view (savedquery). No plan/execute split
/// because there's no payload to mangle — just a viewId. The agent MUST call
/// request_approval before invoking this tool (destructive op, irreversible
/// without a fetchxml/layoutxml snapshot).
/// </summary>
public class DeleteViewTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<DeleteViewTool> logger)
{
    [Description("Delete a Dataverse system view (savedquery) by viewId. DESTRUCTIVE: removes the view from "
        + "every model-driven app that references it. Call request_approval BEFORE this tool. Use "
        + "get_views_list to find the viewId. There is no plan_delete_view — call this directly after "
        + "approval.")]
    public async Task<string> DeleteViewAsync(
        [Description("The GUID of the view to delete (savedqueryid from get_views_list)")] string viewId,
        [Description("The logical name of the table the view belongs to (for audit logging)")] string tableLogicalName,
        [Description("The display name of the view (for audit logging)")] string viewName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("DeleteView executing: viewId={ViewId}, tableLogicalName={TableLogicalName}, viewName={ViewName}",
            viewId, tableLogicalName, viewName);

        if (!Guid.TryParse(viewId, out var parsedViewId) || parsedViewId == Guid.Empty)
        {
            return "ERROR: viewId must be a valid GUID — call get_views_list to retrieve it.";
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            await dataverseClient.DeleteViewAsync(parsedViewId, cancellationToken);

            // Reverse op: not trivially undoable without a pre-delete snapshot of
            // fetchxml/layoutxml/name. Mark as non-reversible. A future enhancement
            // could fetch the view via GetViewsListAsync before delete and store
            // the spec for replay through plan_create_view.
            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                ToolName = "DeleteView",
                Parameters = new { viewId, tableLogicalName, viewName },
                Result = new { viewId },
                IsReversible = false,
                ReverseOp = null
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "DeleteView",
                Detail = $"Deleted view '{viewName}' (viewId={parsedViewId}) from table '{tableLogicalName}'",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("DeleteView succeeded: viewId={ViewId}, viewName={ViewName}", parsedViewId, viewName);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "DeleteView"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"View '{viewName}' (viewId={parsedViewId}) deleted from table '{tableLogicalName}'.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "DeleteView failed: viewId={ViewId}, viewName={ViewName}", parsedViewId, viewName);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "DeleteView",
                Detail = $"Failed to delete view '{viewName}' (viewId={parsedViewId}) on table '{tableLogicalName}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "DeleteView"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to delete view '{viewName}' (viewId={parsedViewId}) on table '{tableLogicalName}': {ex.Message}. "
                + "Check that the view exists and isn't a system view (some are protected). "
                + "You may retry or hand off to Solution Architect for diagnosis.";
        }
    }
}
