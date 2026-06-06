using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ActivateWorkflowTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ActivateWorkflowTool> logger)
{
    [Description("Activate a deactivated Dataverse workflow (Business Process Flow or "
        + "Business Rule). Direct call — no plan/execute split. Pushes an OperationStack "
        + "entry with a Deactivate reverse-op so undo_last_operation can flip it back. "
        + "Idempotent — activating an already-activated workflow is a no-op.")]
    public async Task<string> ActivateWorkflowAsync(
        [Description("WorkflowId GUID — from get_workflows")] Guid workflowId,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        if (workflowId == Guid.Empty)
        {
            return "ERROR: workflowId is required. Call get_workflows first.";
        }

        var live = await dataverseClient.GetWorkflowAsync(workflowId, cancellationToken);
        if (live is null)
        {
            return $"NO_OP: Workflow {workflowId} not found in this environment.";
        }
        if (live.IsActivated)
        {
            return $"NO_OP: Workflow '{live.Name}' ({workflowId}) is already activated.";
        }

        try
        {
            await dataverseClient.ActivateWorkflowAsync(workflowId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "ActivateWorkflow: failed for {WorkflowId}", workflowId);
            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ActivateWorkflow", "error", sw.ElapsedMilliseconds);
            return $"ERROR: Failed to activate workflow '{live.Name}' ({workflowId}): {ex.Message}";
        }

        await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
        {
            fresh.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.AutomationBuilder,
                ToolName = "ActivateWorkflow",
                Parameters = new { workflowId, workflowName = live.Name },
                Result = new { workflowId, activated = true },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "DeactivateWorkflow",
                    Parameters = new { workflowId, workflowName = live.Name },
                    Warning = "Deactivation pauses the workflow; in-progress instances are unaffected but new triggers stop firing.",
                    RiskLevel = RiskLevel.Medium,
                },
            });
            fresh.AuditLog.Add(AuditEntry.Ok(AgentNames.AutomationBuilder, "ActivateWorkflow", $"Activated workflow '{live.Name}' ({workflowId}, category={live.Category})"));
        }, cancellationToken);

        sw.Stop();
        ForgeActivitySource.RecordToolDuration("ActivateWorkflow", "success", sw.ElapsedMilliseconds);
        return $"Workflow '{live.Name}' ({workflowId}) activated. End the turn after reporting — "
            + "activation state is a single bool, no Tester handoff needed.";
    }
}
