using DataverseForge.Application.Agents;
using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteUpdateOptionTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteUpdateOptionTool> logger)
{
    [Description("Execute the rename or delete of an existing option value. Pass the planId from "
        + "plan_update_option — the payload was stored server-side and will be loaded automatically.")]
    public async Task<string> ExecuteUpdateOptionAsync(
        [Description("planId returned by plan_update_option — must match exactly")] string planId,
        [Description("Action: 'update' (rename) or 'delete' (remove). Must match the plan's action.")] string action,
        [Description("Option set name or table.field for logging/audit")] string locator,
        [Description("Integer value of the option being changed (for logging/audit)")] int optionValue,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteUpdateOption executing: planId={PlanId}, action={Action}, locator={Locator}, value={OptionValue}",
            planId, action, locator, optionValue);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (!state.PendingPlans.TryGetValue(planId, out var webApiPayloadJson))
        {
            return $"ERROR: Plan '{planId}' not found. Call plan_update_option first.";
        }

        var actionLower = action.Trim().ToLowerInvariant();

        try
        {
            if (actionLower == "update")
            {
                await dataverseClient.UpdateOptionValueAsync(webApiPayloadJson, cancellationToken);
            }
            else if (actionLower == "delete")
            {
                await dataverseClient.DeleteOptionValueAsync(webApiPayloadJson, cancellationToken);
            }
            else
            {
                return $"ERROR: Unknown action '{action}'. Expected 'update' or 'delete'.";
            }

            state.PendingPlans.Remove(planId);

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                ToolName = "ExecuteUpdateOption",
                Parameters = new { action = actionLower, locator, optionValue },
                Result = new { optionValue },
                IsReversible = false,
                ReverseOp = null
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "ExecuteUpdateOption",
                Detail = $"{actionLower} option value {optionValue} on {locator}",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteUpdateOption"), new KeyValuePair<string, object?>("tool.status", "success"));
            var verb = actionLower == "update" ? "renamed" : "deleted";
            return $"Option value {optionValue} on {locator} {verb}. "
                + "NEXT REQUIRED ACTION: call handoff_to_tester (or continue with the next plan in the batch) — do NOT linger.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ExecuteUpdateOption failed: action={Action}, locator={Locator}, value={OptionValue}", actionLower, locator, optionValue);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "ExecuteUpdateOption",
                Detail = $"Failed to {actionLower} option {optionValue} on {locator}: {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteUpdateOption"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to {actionLower} option {optionValue} on {locator}: {ex.Message}";
        }
    }
}
