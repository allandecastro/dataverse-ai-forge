using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Application.Agents;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteAddStatusOptionTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteAddStatusOptionTool> logger)
{
    [Description("Execute the addition of a new status-reason option to a table's `statuscode` field. "
        + "Pass the planId from plan_add_status_option — the InsertStatusValue payload was stored "
        + "server-side and will be loaded automatically.")]
    public async Task<string> ExecuteAddStatusOptionAsync(
        [Description("planId returned by plan_add_status_option — must match exactly")] string planId,
        [Description("Logical name of the table (for logging/audit)")] string tableLogicalName,
        [Description("State value the new status maps to: 0 = Active, 1 = Inactive (for logging/audit)")] int stateValue,
        [Description("Integer value of the new status reason (for logging/audit)")] int newStatusValue,
        [Description("Display label of the new status reason (for logging/audit)")] string newStatusLabel,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteAddStatusOption executing: planId={PlanId}, table={TableLogicalName}, state={StateValue}, value={NewStatusValue}",
            planId, tableLogicalName, stateValue, newStatusValue);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (!state.PendingPlans.TryGetValue(planId, out var webApiPayloadJson))
        {
            return $"ERROR: Plan '{planId}' not found. Call plan_add_status_option first.";
        }

        try
        {
            await dataverseClient.InsertStatusValueAsync(webApiPayloadJson, cancellationToken);
            state.PendingPlans.Remove(planId);

            var stateLabel = stateValue == 0 ? "Active" : stateValue == 1 ? "Inactive" : $"State={stateValue}";

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                ToolName = "ExecuteAddStatusOption",
                Parameters = new { tableLogicalName, stateValue, newStatusValue, newStatusLabel },
                Result = new { newStatusValue },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "DeleteOptionValue",
                    Parameters = new { tableLogicalName, fieldLogicalName = "statuscode", value = newStatusValue },
                    Warning = "Deleting a status option may break records that already use this status.",
                    RiskLevel = RiskLevel.Medium
                }
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "ExecuteAddStatusOption",
                Detail = $"Added status reason {newStatusValue} ('{newStatusLabel}', mapped to {stateLabel}) on {tableLogicalName}.statuscode",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteAddStatusOption"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"Status reason {newStatusValue} ('{newStatusLabel}', {stateLabel}) added to {tableLogicalName.ToLowerInvariant()}.statuscode. "
                + "NEXT REQUIRED ACTION: call handoff_to_tester (or continue with the next plan in the batch) — do NOT linger.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ExecuteAddStatusOption failed: table={TableLogicalName}, value={NewStatusValue}", tableLogicalName, newStatusValue);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "ExecuteAddStatusOption",
                Detail = $"Failed to add status reason {newStatusValue} on {tableLogicalName}.statuscode: {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteAddStatusOption"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to add status reason {newStatusValue}: {ex.Message}";
        }
    }
}
