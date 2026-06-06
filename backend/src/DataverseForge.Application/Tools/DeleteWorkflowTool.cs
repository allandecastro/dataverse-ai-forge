using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class DeleteWorkflowTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<DeleteWorkflowTool> logger)
{
    [Description("Delete a Dataverse workflow (Business Process Flow OR Business Rule). DIRECT "
        + "call — no plan/execute split (matches delete_field, delete_security_role). REFUSES "
        + "on: (1) managed workflows, (2) activated workflows (must call deactivate_workflow "
        + "first — Dataverse refuses delete on activated workflows). Records a non-reversible "
        + "OperationStack entry: undoing would recreate a workflow but the new GUID + fresh "
        + "definition mean it's not a true restore (Business Rules can be re-planned + "
        + "executed; BPFs would need to be re-built in the maker portal).")]
    public async Task<string> DeleteWorkflowAsync(
        [Description("WorkflowId GUID — from get_workflows")] Guid workflowId,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        if (workflowId == Guid.Empty)
        {
            return "ERROR: workflowId is required.";
        }

        var live = await dataverseClient.GetWorkflowAsync(workflowId, cancellationToken);
        if (live is null)
        {
            return $"NO_OP: Workflow {workflowId} not found in this environment.";
        }

        if (live.IsManaged)
        {
            return $"REFUSED: Workflow '{live.Name}' is managed (shipped via a managed "
                + "solution). Managed workflows can't be deleted via the Web API — direct the "
                + "user to remove the managed solution itself, or to the maker portal.";
        }

        if (live.IsActivated)
        {
            return $"REFUSED: Workflow '{live.Name}' is activated. Dataverse refuses to delete "
                + $"activated workflows. Call deactivate_workflow({workflowId}) first, then "
                + "retry delete.";
        }

        try
        {
            await dataverseClient.DeleteWorkflowAsync(workflowId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "DeleteWorkflow: failed for {WorkflowId}", workflowId);
            sw.Stop();
            ForgeActivitySource.RecordToolDuration("DeleteWorkflow", "error", sw.ElapsedMilliseconds);
            return $"ERROR: Failed to delete workflow '{live.Name}': {ex.Message}";
        }

        await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
        {
            fresh.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.AutomationBuilder,
                ToolName = "DeleteWorkflow",
                Parameters = new { workflowId, workflowName = live.Name, category = (int)live.Category },
                Result = new { workflowId, workflowName = live.Name },
                IsReversible = false,
                ReverseOp = null,
            });
            fresh.AuditLog.Add(AuditEntry.Ok(AgentNames.AutomationBuilder, "DeleteWorkflow", $"Deleted workflow '{live.Name}' ({workflowId}, category={live.Category})"));
        }, cancellationToken);

        sw.Stop();
        ForgeActivitySource.RecordToolDuration("DeleteWorkflow", "success", sw.ElapsedMilliseconds);
        return $"Workflow '{live.Name}' ({workflowId}, category={live.Category}) deleted. "
            + "End the turn after reporting — there's nothing for the Tester to verify on a delete.";
    }
}
