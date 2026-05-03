using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreatePolymorphicLookupTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreatePolymorphicLookupTool> logger)
{
    [Description("Execute a polymorphic (multi-table) lookup creation. Pass the planId from "
        + "plan_create_polymorphic_lookup — the OData JSON payload was stored server-side and "
        + "is loaded automatically. Do NOT construct the OData JSON yourself. The action atomically "
        + "creates the lookup column and one 1:N relationship per target table.")]
    public async Task<string> ExecuteCreatePolymorphicLookupAsync(
        [Description("planId returned by plan_create_polymorphic_lookup — must match exactly. Payload is "
            + "stored server-side; do NOT construct the OData JSON yourself.")] string planId,
        [Description("Logical name of the referencing table (used for the success message + audit)")] string referencingTable,
        [Description("Comma-separated logical names of the target tables (used for the success message + audit)")] string referencedTables,
        [Description("Schema name of the lookup column (used for the success message + audit)")] string lookupSchemaName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var t = referencingTable.ToLowerInvariant();
        logger.LogInformation("ExecuteCreatePolymorphicLookup: planId={PlanId}, referencing={Ref}, lookup={Lookup}",
            planId, t, lookupSchemaName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (planId.StartsWith("rejected_polymorphic_lookup_", StringComparison.Ordinal))
        {
            return $"SKIPPED: Plan '{planId}' was rejected by plan_create_polymorphic_lookup. "
                + "Fix the input and re-plan; do NOT execute this planId.";
        }

        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            logger.LogInformation("ExecuteCreatePolymorphicLookup: planId {PlanId} already executed earlier this session", planId);
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier this session. "
                + $"The polymorphic lookup '{lookupSchemaName}' is already on '{t}'. DO NOT retry, DO NOT re-plan. "
                + "Move on to the NEXT pending plan in this turn.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            logger.LogWarning("ExecuteCreatePolymorphicLookup: planId not found: {PlanId}", planId);
            return $"ERROR: Plan '{planId}' not found. Call plan_create_polymorphic_lookup first and pass the returned planId verbatim.";
        }

        try
        {
            var attributeId = await dataverseClient.CreatePolymorphicLookupAsync(webApiPayloadJson, cancellationToken);
            state.ExecutedPlans[planId] = webApiPayloadJson;
            state.PendingPlans.Remove(planId);

            // Reverse op: not trivially undoable. Removing the lookup means
            // deleting N relationships in the right order, and Dataverse may
            // refuse if any rows already use the lookup. Mark non-reversible.
            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                ToolName = "ExecuteCreatePolymorphicLookup",
                Parameters = new { referencingTable = t, referencedTables, lookupSchemaName },
                Result = new { attributeId, lookupSchemaName },
                IsReversible = false,
                ReverseOp = null
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "ExecuteCreatePolymorphicLookup",
                Detail = $"Created polymorphic lookup '{lookupSchemaName}' on '{t}' targeting [{referencedTables}] (AttributeId={attributeId})",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreatePolymorphicLookup"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"Polymorphic lookup '{lookupSchemaName}' created on table '{t}' targeting [{referencedTables}] "
                + $"(AttributeId={attributeId}). One 1:N relationship per target table was created automatically. "
                + "NEXT REQUIRED ACTION: call handoff_to_tester so the Tester can verify the lookup.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteCreatePolymorphicLookup failed: lookup={Lookup}", lookupSchemaName);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "ExecuteCreatePolymorphicLookup",
                Detail = $"Failed to create polymorphic lookup '{lookupSchemaName}' on '{t}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreatePolymorphicLookup"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to create polymorphic lookup '{lookupSchemaName}': {ex.Message}. "
                + "Common causes: a target table doesn't exist, the schema name conflicts with an existing column, "
                + "or one of the relationship schema names already exists. Discard this plan and re-plan with corrected names.";
        }
    }
}
