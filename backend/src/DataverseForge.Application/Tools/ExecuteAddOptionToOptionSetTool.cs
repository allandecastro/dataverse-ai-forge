using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Application.Agents;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteAddOptionToOptionSetTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteAddOptionToOptionSetTool> logger)
{
    [Description("Execute the addition of a new option value to a Dataverse option set. Pass the "
        + "planId from plan_add_option_to_option_set — the payload was stored server-side and "
        + "will be loaded automatically. Do NOT try to construct the payload yourself.")]
    public async Task<string> ExecuteAddOptionToOptionSetAsync(
        [Description("planId returned by plan_add_option_to_option_set — must match exactly")] string planId,
        [Description("Name of the option set (for logging/audit only)")] string optionSetName,
        [Description("True for global option set, false for local")] bool isGlobal,
        [Description("Integer value of the new option (for logging/audit only)")] int newOptionValue,
        [Description("Display label of the new option (for logging/audit only)")] string newOptionLabel,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteAddOptionToOptionSet executing: planId={PlanId}, optionSetName={OptionSetName}, value={NewOptionValue}",
            planId, optionSetName, newOptionValue);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (!state.PendingPlans.TryGetValue(planId, out var webApiPayloadJson))
        {
            return $"ERROR: Plan '{planId}' not found. Call plan_add_option_to_option_set first.";
        }

        try
        {
            await dataverseClient.InsertOptionValueAsync(webApiPayloadJson, cancellationToken);
            state.PendingPlans.Remove(planId);

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                ToolName = "ExecuteAddOptionToOptionSet",
                Parameters = new { optionSetName, isGlobal, newOptionValue, newOptionLabel },
                Result = new { newOptionValue },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "DeleteOptionValue",
                    Parameters = new { optionSetName, newOptionValue },
                    Warning = "Deleting an option value may cause data loss if existing records use this value.",
                    RiskLevel = RiskLevel.Medium
                }
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "ExecuteAddOptionToOptionSet",
                Detail = $"Added option value {newOptionValue} ('{newOptionLabel}') to {(isGlobal ? "global" : "local")} option set '{optionSetName}'",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            var scope = isGlobal ? $"global option set '{optionSetName}'" : $"local option set '{optionSetName}'";
            logger.LogInformation("ExecuteAddOptionToOptionSet succeeded: optionSetName={OptionSetName}, value={NewOptionValue}", optionSetName, newOptionValue);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteAddOptionToOptionSet"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"Option value {newOptionValue} ('{newOptionLabel}') added to {scope}. "
                + "NEXT REQUIRED ACTION: call handoff_to_tester (or continue with the next plan in the batch) — do NOT linger.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ExecuteAddOptionToOptionSet failed: optionSetName={OptionSetName}, value={NewOptionValue}", optionSetName, newOptionValue);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "ExecuteAddOptionToOptionSet",
                Detail = $"Failed to add option value {newOptionValue} to option set '{optionSetName}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteAddOptionToOptionSet"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to add option value {newOptionValue} to option set '{optionSetName}': {ex.Message}";
        }
    }
}
