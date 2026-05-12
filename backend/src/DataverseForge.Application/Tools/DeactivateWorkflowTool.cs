using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class DeactivateWorkflowTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<DeactivateWorkflowTool> logger)
{
    [Description("Deactivate an activated Dataverse workflow (Business Process Flow or "
        + "Business Rule). Direct call — no plan/execute split. Pushes an OperationStack "
        + "entry with an Activate reverse-op. Required before delete_workflow on activated "
        + "workflows. Idempotent — deactivating an already-deactivated workflow is a no-op.")]
    public async Task<string> DeactivateWorkflowAsync(
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
        if (!live.IsActivated)
        {
            return $"NO_OP: Workflow '{live.Name}' ({workflowId}) is already deactivated.";
        }

        try
        {
            await dataverseClient.DeactivateWorkflowAsync(workflowId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "DeactivateWorkflow: failed for {WorkflowId}", workflowId);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("tool.name", "DeactivateWorkflow"),
                new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to deactivate workflow '{live.Name}' ({workflowId}): {ex.Message}";
        }

        await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
        {
            fresh.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.AutomationBuilder,
                ToolName = "DeactivateWorkflow",
                Parameters = new { workflowId, workflowName = live.Name },
                Result = new { workflowId, activated = false },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "ActivateWorkflow",
                    Parameters = new { workflowId, workflowName = live.Name },
                    Warning = "Re-activation will resume new triggers; mid-flight state isn't restored.",
                    RiskLevel = RiskLevel.Medium,
                },
            });
            fresh.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.AutomationBuilder,
                Action = "DeactivateWorkflow",
                Detail = $"Deactivated workflow '{live.Name}' ({workflowId}, category={live.Category})",
                Success = true,
            });
        }, cancellationToken);

        sw.Stop();
        ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds,
            new KeyValuePair<string, object?>("tool.name", "DeactivateWorkflow"),
            new KeyValuePair<string, object?>("tool.status", "success"));
        return $"Workflow '{live.Name}' ({workflowId}) deactivated. End the turn after reporting.";
    }
}
