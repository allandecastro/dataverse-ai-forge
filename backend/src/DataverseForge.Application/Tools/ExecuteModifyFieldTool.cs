using DataverseForge.Application.Agents;
using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteModifyFieldTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteModifyFieldTool> logger)
{
    [Description("Execute the modification of an existing field. Pass the planId from "
        + "plan_modify_field — the OData JSON payload was stored server-side and will be "
        + "loaded automatically. Do NOT try to construct the payload yourself. The target "
        + "table and field are identified by their LOGICAL names — never pass MetadataId GUIDs.")]
    public async Task<string> ExecuteModifyFieldAsync(
        [Description("planId returned by plan_modify_field — must match exactly")] string planId,
        [Description("Logical name of the table (e.g. 'eppc_gift'). The MetadataId is resolved server-side.")] string tableLogicalName,
        [Description("Logical name of the field (e.g. 'eppc_giftname'). The MetadataId is resolved server-side.")] string fieldLogicalName,
        [Description("Summary of what changed (e.g., 'displayName, requiredLevel')")] string changeSummary,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteModifyField executing: planId={PlanId}, table={TableLogicalName}, field={FieldLogicalName}",
            planId, tableLogicalName, fieldLogicalName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (!state.PendingPlans.TryGetValue(planId, out var webApiPayloadJson))
        {
            return $"ERROR: Plan '{planId}' not found. Call plan_modify_field first and pass its planId verbatim.";
        }

        if (string.IsNullOrWhiteSpace(tableLogicalName) || string.IsNullOrWhiteSpace(fieldLogicalName))
        {
            return "ERROR: tableLogicalName and fieldLogicalName are required.";
        }

        try
        {
            await dataverseClient.ModifyFieldAsync(tableLogicalName, fieldLogicalName, webApiPayloadJson, cancellationToken);
            state.PendingPlans.Remove(planId);

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                ToolName = "ExecuteModifyField",
                Parameters = new { tableLogicalName, fieldLogicalName, changeSummary },
                Result = new { fieldLogicalName, changeSummary },
                IsReversible = false,
                ReverseOp = null
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "ExecuteModifyField",
                Detail = $"Modified field '{fieldLogicalName}' on table '{tableLogicalName}'. Changes: {changeSummary}",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("ExecuteModifyField succeeded: fieldLogicalName={FieldLogicalName} on {TableLogicalName}", fieldLogicalName, tableLogicalName);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteModifyField"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"Field '{fieldLogicalName.ToLowerInvariant()}' on table '{tableLogicalName.ToLowerInvariant()}' modified successfully. Changes: {changeSummary}. "
                + "NEXT REQUIRED ACTION: call handoff_to_tester so the Tester can verify the field changes.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ExecuteModifyField failed: tableLogicalName={TableLogicalName}, fieldLogicalName={FieldLogicalName}", tableLogicalName, fieldLogicalName);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "ExecuteModifyField",
                Detail = $"Failed to modify field '{fieldLogicalName}' on table '{tableLogicalName}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteModifyField"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to modify field '{fieldLogicalName}' on table '{tableLogicalName}': {ex.Message}";
        }
    }
}
