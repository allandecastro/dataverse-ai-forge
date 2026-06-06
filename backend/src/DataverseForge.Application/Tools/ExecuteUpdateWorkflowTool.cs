using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteUpdateWorkflowTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteUpdateWorkflowTool> logger)
{
    [Description("Execute a workflow rename / description-change. Pass the planId from "
        + "plan_update_workflow — the OData PATCH body was stored server-side and is loaded "
        + "automatically. Works for both Business Process Flows and Business Rules (same "
        + "Dataverse entity).")]
    public async Task<string> ExecuteUpdateWorkflowAsync(
        [Description("planId returned by plan_update_workflow — must match exactly")] string planId,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteUpdateWorkflow executing: planId={PlanId}", planId);

        if (planId.StartsWith("rejected_update_workflow_", StringComparison.Ordinal))
        {
            return $"SKIPPED: Plan '{planId}' was rejected by plan_update_workflow. Re-plan with "
                + "corrected inputs.";
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        var lookupStatus = state.LookupPlan(planId, out var compositeJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier in this session.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            return $"ERROR: Plan '{planId}' not found. Call plan_update_workflow first.";
        }

        var doc = JsonDocument.Parse(compositeJson);
        var workflowId = doc.RootElement.GetProperty("workflowId").GetGuid();
        var patchJson = doc.RootElement.GetProperty("patch").GetRawText();

        try
        {
            await dataverseClient.UpdateWorkflowAsync(workflowId, patchJson, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "ExecuteUpdateWorkflow: PATCH failed for {WorkflowId}", workflowId);
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(AuditEntry.Failed(AgentNames.AutomationBuilder, "ExecuteUpdateWorkflow", $"Failed to PATCH workflow {workflowId}: {ex.Message}"));
            }, cancellationToken);
            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ExecuteUpdateWorkflow", "error", sw.ElapsedMilliseconds);
            return $"ERROR: Failed to update workflow {workflowId}: {ex.Message}";
        }

        await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
        {
            fresh.ExecutedPlans[planId] = compositeJson;
            fresh.PendingPlans.Remove(planId);
            fresh.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.AutomationBuilder,
                ToolName = "ExecuteUpdateWorkflow",
                Parameters = new { workflowId, planId },
                Result = new { workflowId, planId },
                IsReversible = false,
                ReverseOp = null,
            });
            fresh.AuditLog.Add(AuditEntry.Ok(AgentNames.AutomationBuilder, "ExecuteUpdateWorkflow", $"Updated workflow {workflowId}"));
        }, cancellationToken);

        sw.Stop();
        ForgeActivitySource.RecordToolDuration("ExecuteUpdateWorkflow", "success", sw.ElapsedMilliseconds);
        return $"Workflow {workflowId} updated. "
            + $"NEXT REQUIRED ACTION: call handoff_to_tester with planId={planId} so the Tester "
            + "can verify the rename via verify_workflow_against_plan.";
    }
}
