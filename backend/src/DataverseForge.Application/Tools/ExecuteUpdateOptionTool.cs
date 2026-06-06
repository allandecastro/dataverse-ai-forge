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

        var actionLower = action.Trim().ToLowerInvariant();

        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            logger.LogInformation("ExecuteUpdateOption: planId {PlanId} already executed earlier this session", planId);
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier in this session. "
                + $"Option value {optionValue} on {locator} was already {(actionLower == "delete" ? "deleted" : "renamed")}. "
                + "DO NOT retry, DO NOT re-plan. Move on to the NEXT pending plan.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            return $"ERROR: Plan '{planId}' not found. Call plan_update_option first.";
        }

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

            // F-23: re-read state right before mutate+save.
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.ExecutedPlans[planId] = webApiPayloadJson;
                fresh.PendingPlans.Remove(planId);
                fresh.OperationStack.Push(new ExecutedOperation
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
                fresh.AuditLog.Add(AuditEntry.Ok(AgentNames.DataModelBuilder, "ExecuteUpdateOption", $"{actionLower} option value {optionValue} on {locator}"));
            }, cancellationToken);

            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ExecuteUpdateOption", "success", sw.ElapsedMilliseconds);
            var verb = actionLower == "update" ? "renamed" : "deleted";
            return $"Option value {optionValue} on {locator} {verb}. "
                + "NEXT REQUIRED ACTION: call handoff_to_tester (or continue with the next plan in the batch) — do NOT linger.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ExecuteUpdateOption failed: action={Action}, locator={Locator}, value={OptionValue}", actionLower, locator, optionValue);

            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(AuditEntry.Failed(AgentNames.DataModelBuilder, "ExecuteUpdateOption", $"Failed to {actionLower} option {optionValue} on {locator}: {ex.Message}"));
            }, cancellationToken);

            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ExecuteUpdateOption", "error", sw.ElapsedMilliseconds);
            return $"ERROR: Failed to {actionLower} option {optionValue} on {locator}: {ex.Message}";
        }
    }
}
