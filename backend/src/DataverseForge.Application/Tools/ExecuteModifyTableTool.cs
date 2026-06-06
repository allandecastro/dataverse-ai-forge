using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteModifyTableTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteModifyTableTool> logger)
{
    [Description("Execute a previously planned table-definition modification (e.g., enabling Quick "
        + "Create, audit, queues, mobile visibility, renames). Pass the planId from plan_modify_table — "
        + "the OData JSON payload was stored server-side and is loaded automatically. Do NOT construct "
        + "the OData JSON yourself.")]
    public async Task<string> ExecuteModifyTableAsync(
        [Description("planId returned by plan_modify_table — must match exactly. Payload is stored "
            + "server-side; do NOT construct the OData JSON yourself.")] string planId,
        [Description("The logical name of the table being modified (used for audit + the URL)")] string tableLogicalName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var t = tableLogicalName.ToLowerInvariant();
        logger.LogInformation("ExecuteModifyTable executing: planId={PlanId}, table={Table}", planId, t);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (planId.StartsWith("rejected_modify_table_", StringComparison.Ordinal))
        {
            return $"SKIPPED: Plan '{planId}' was rejected by plan_modify_table (table not found or "
                + "no valid modifications). Fix the input and re-plan; do NOT execute this planId.";
        }

        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            logger.LogInformation("ExecuteModifyTable: planId {PlanId} already executed earlier this session", planId);
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier this session. "
                + $"Table '{t}' is already modified. DO NOT retry.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            logger.LogWarning("ExecuteModifyTable: planId not found in PendingPlans: {PlanId}", planId);
            return $"ERROR: Plan '{planId}' not found. Call plan_modify_table first and pass its returned planId verbatim.";
        }

        try
        {
            await dataverseClient.ModifyTableAsync(t, webApiPayloadJson, cancellationToken);

            // Reverse op: not trivially undoable — would require snapshotting the
            // prior table definition. Mark as non-reversible.
            //
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
                    ToolName = "ExecuteModifyTable",
                    Parameters = new { tableLogicalName = t },
                    Result = new { tableLogicalName = t },
                    IsReversible = false,
                    ReverseOp = null
                });
                fresh.AuditLog.Add(AuditEntry.Ok(AgentNames.DataModelBuilder, "ExecuteModifyTable", $"Modified table '{t}' definition"));
            }, cancellationToken);

            logger.LogInformation("ExecuteModifyTable succeeded: table={Table}", t);
            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ExecuteModifyTable", "success", sw.ElapsedMilliseconds);
            return $"Table '{t}' definition updated. NEXT REQUIRED ACTION: call handoff_to_tester so the "
                + "Tester can verify the table changes.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteModifyTable failed: table={Table}", t);

            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(AuditEntry.Failed(AgentNames.DataModelBuilder, "ExecuteModifyTable", $"Failed to modify table '{t}': {ex.Message}"));
            }, cancellationToken);

            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ExecuteModifyTable", "error", sw.ElapsedMilliseconds);
            return $"ERROR: Failed to modify table '{t}': {ex.Message}. "
                + "Common causes: trying to disable an enable-only capability (HasNotes, HasActivities, "
                + "IsBusinessProcessEnabled), the table is part of a managed solution, or a managed-property "
                + "guard blocks the change. You may retry or hand off to Solution Architect for diagnosis.";
        }
    }
}
