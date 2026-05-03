using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateCustomerLookupTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateCustomerLookupTool> logger)
{
    [Description("Execute a Customer (Account+Contact polymorphic) lookup creation. Pass the planId from "
        + "plan_create_customer_lookup — the OData JSON payload was stored server-side and is loaded "
        + "automatically. Do NOT construct the OData JSON yourself. The action atomically creates the "
        + "Customer column and the two 1:N relationships (one to Account, one to Contact).")]
    public async Task<string> ExecuteCreateCustomerLookupAsync(
        [Description("planId returned by plan_create_customer_lookup — must match exactly. Payload is "
            + "stored server-side; do NOT construct the OData JSON yourself.")] string planId,
        [Description("Logical name of the referencing table (used for the success message + audit)")] string referencingTable,
        [Description("Schema name of the Customer column (used for the success message + audit)")] string lookupSchemaName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var t = referencingTable.ToLowerInvariant();
        logger.LogInformation("ExecuteCreateCustomerLookup: planId={PlanId}, referencing={Ref}, lookup={Lookup}",
            planId, t, lookupSchemaName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (planId.StartsWith("rejected_customer_lookup_", StringComparison.Ordinal))
        {
            return $"SKIPPED: Plan '{planId}' was rejected by plan_create_customer_lookup. "
                + "Fix the input and re-plan; do NOT execute this planId.";
        }

        var lookupStatus = state.LookupPlan(planId, out var webApiPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            logger.LogInformation("ExecuteCreateCustomerLookup: planId {PlanId} already executed earlier this session", planId);
            return $"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier this session. "
                + $"The Customer column '{lookupSchemaName}' is already on '{t}'. DO NOT retry, DO NOT re-plan. "
                + "Move on to the NEXT pending plan in this turn.";
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            logger.LogWarning("ExecuteCreateCustomerLookup: planId not found: {PlanId}", planId);
            return $"ERROR: Plan '{planId}' not found. Call plan_create_customer_lookup first and pass the returned planId verbatim.";
        }

        try
        {
            var attributeId = await dataverseClient.CreateCustomerLookupAsync(webApiPayloadJson, cancellationToken);
            state.ExecutedPlans[planId] = webApiPayloadJson;
            state.PendingPlans.Remove(planId);

            // Same reasoning as the polymorphic-lookup undo: removing requires
            // deleting two relationships in order, and the platform may refuse
            // if rows already use the column. Mark non-reversible.
            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                ToolName = "ExecuteCreateCustomerLookup",
                Parameters = new { referencingTable = t, lookupSchemaName },
                Result = new { attributeId, lookupSchemaName },
                IsReversible = false,
                ReverseOp = null
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "ExecuteCreateCustomerLookup",
                Detail = $"Created Customer column '{lookupSchemaName}' on '{t}' (AttributeId={attributeId})",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateCustomerLookup"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"Customer column '{lookupSchemaName}' created on table '{t}' "
                + $"(AttributeId={attributeId}). Both Account and Contact relationships were created automatically. "
                + "NEXT REQUIRED ACTION: call handoff_to_tester so the Tester can verify the column.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteCreateCustomerLookup failed: lookup={Lookup}", lookupSchemaName);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "ExecuteCreateCustomerLookup",
                Detail = $"Failed to create Customer column '{lookupSchemaName}' on '{t}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateCustomerLookup"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to create Customer column '{lookupSchemaName}': {ex.Message}. "
                + "Common causes: the schema name conflicts with an existing column on this table, or one of "
                + "the relationship schema names already exists. Discard this plan and re-plan with corrected names.";
        }
    }
}
