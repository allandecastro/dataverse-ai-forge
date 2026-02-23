using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain;
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
    [Description("Execute the addition of a new option value to an existing Dataverse option set using a pre-built OData JSON payload "
        + "from plan_add_option_to_option_set. Inserts the option value, updates project state, "
        + "and pushes to the operation stack.")]
    public async Task<string> ExecuteAddOptionToOptionSetAsync(
        [Description("The full OData JSON payload from plan_add_option_to_option_set")] string webApiPayloadJson,
        [Description("The name of the option set")] string optionSetName,
        [Description("True for global option set, false for local")] bool isGlobal,
        [Description("The integer value of the new option")] int newOptionValue,
        [Description("The display label of the new option")] string newOptionLabel,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteAddOptionToOptionSet executing: optionSetName={OptionSetName}, isGlobal={IsGlobal}, newOptionValue={NewOptionValue}, newOptionLabel={NewOptionLabel}",
            optionSetName, isGlobal, newOptionValue, newOptionLabel);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            await dataverseClient.InsertOptionValueAsync(webApiPayloadJson, cancellationToken);

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
            logger.LogInformation("ExecuteAddOptionToOptionSet succeeded: optionSetName={OptionSetName}, newOptionValue={NewOptionValue}", optionSetName, newOptionValue);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteAddOptionToOptionSet"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"Option value {newOptionValue} ('{newOptionLabel}') added to {scope}.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteAddOptionToOptionSet failed: optionSetName={OptionSetName}, newOptionValue={NewOptionValue}", optionSetName, newOptionValue);

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
            return $"ERROR: Failed to add option value {newOptionValue} to option set '{optionSetName}': {ex.Message}. "
                + "Check that the payload is valid and the option set exists. "
                + "You may retry or hand off to Solution Architect for diagnosis.";
        }
    }
}
