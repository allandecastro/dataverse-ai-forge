using DataverseForge.Application.Agents;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Executes a reference-data batch planned by <see cref="PlanPopulateReferenceDataTool"/>.
/// Uses the planId-by-reference pattern (CLAUDE.md recurring bug #1) — the
/// payload (multi-KB OData JSON) is stored server-side under the planId and
/// the agent never round-trips it through its context.
/// </summary>
public class ExecutePopulateReferenceDataTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecutePopulateReferenceDataTool> logger)
{
    [Description("Execute batch creation of reference/seed data records using the planId from "
        + "plan_populate_reference_data — the OData payload was stored server-side and will be "
        + "loaded automatically. Do NOT construct or pass the payload yourself.")]
    public async Task<BatchOperationResult> ExecutePopulateReferenceDataAsync(
        [Description("planId from plan_populate_reference_data — must match exactly. The "
            + "WebApiBatchPayloadJson is stored server-side under this id.")]
        string planId,
        [Description("The entity set name for the OData endpoint (must match what the plan stored).")]
        string entitySetName,
        [Description("The table logical name (must match what the plan stored).")]
        string tableLogicalName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecutePopulateReferenceData executing: planId={PlanId}, entitySetName={EntitySetName}, tableLogicalName={TableLogicalName}",
            planId, entitySetName, tableLogicalName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (planId.StartsWith("rejected_", StringComparison.Ordinal))
        {
            return new BatchOperationResult(
                SuccessCount: 0,
                FailureCount: 0,
                CreatedRecordIds: Array.Empty<Guid>(),
                Errors: [$"SKIPPED: Plan '{planId}' was rejected by plan_populate_reference_data. "
                    + "Fix the records and re-plan; do NOT execute this planId."]);
        }

        var lookupStatus = state.LookupPlan(planId, out var batchPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            return new BatchOperationResult(
                SuccessCount: 0,
                FailureCount: 0,
                CreatedRecordIds: Array.Empty<Guid>(),
                Errors: [$"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier this session. "
                    + $"Records were inserted into '{entitySetName}'. DO NOT retry, DO NOT re-plan."]);
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            return new BatchOperationResult(
                SuccessCount: 0,
                FailureCount: 0,
                CreatedRecordIds: Array.Empty<Guid>(),
                Errors: [$"ERROR: Plan '{planId}' not found. Call plan_populate_reference_data first "
                    + "and pass its returned planId verbatim."]);
        }

        // Stored payload shape: { "entitySetName": "...", "records": ["{json}", ...] }.
        // Extract the records array — same shape PlanGenerateTestData uses, so we
        // could share a helper later if a third caller appears.
        List<string> recordPayloads;
        try
        {
            using var doc = JsonDocument.Parse(batchPayloadJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("records", out var recordsEl)
                && recordsEl.ValueKind == JsonValueKind.Array)
            {
                recordPayloads = new List<string>(recordsEl.GetArrayLength());
                foreach (var el in recordsEl.EnumerateArray())
                {
                    recordPayloads.Add(el.ValueKind == JsonValueKind.String
                        ? el.GetString() ?? ""
                        : el.GetRawText());
                }
            }
            else
            {
                recordPayloads = [];
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "ExecutePopulateReferenceData: stored payload was not parseable JSON for plan {PlanId}", planId);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecutePopulateReferenceData"), new KeyValuePair<string, object?>("tool.status", "error"));
            return new BatchOperationResult(
                SuccessCount: 0,
                FailureCount: 0,
                CreatedRecordIds: Array.Empty<Guid>(),
                Errors: [$"Internal: stored payload for plan '{planId}' was not parseable JSON: {ex.Message}."]);
        }
        if (recordPayloads.Count == 0)
        {
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecutePopulateReferenceData"), new KeyValuePair<string, object?>("tool.status", "error"));
            return new BatchOperationResult(
                SuccessCount: 0,
                FailureCount: 0,
                CreatedRecordIds: Array.Empty<Guid>(),
                Errors: [$"Plan '{planId}' has no records to insert. Re-call plan_populate_reference_data."]);
        }

        try
        {
            var result = await dataverseClient.CreateRecordsBatchAsync(entitySetName, recordPayloads.AsReadOnly(), cancellationToken);

            state.ExecutedPlans[planId] = batchPayloadJson;
            state.PendingPlans.Remove(planId);
            state.PendingPlanTimestamps.Remove(planId);

            foreach (var recordId in result.CreatedRecordIds)
            {
                state.SeedDataRecords.Add(new CreatedRecord(recordId, tableLogicalName, "Reference data", DateTime.UtcNow));
            }

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.DataPopulation,
                ToolName = "ExecutePopulateReferenceData",
                Parameters = new { planId, entitySetName, tableLogicalName, recordCount = recordPayloads.Count },
                Result = new { result.SuccessCount, result.FailureCount, recordIds = result.CreatedRecordIds },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "DeleteRecords",
                    Parameters = new { entitySetName, recordIds = result.CreatedRecordIds },
                    Warning = $"Deleting {result.SuccessCount} reference data records from {tableLogicalName}.",
                    RiskLevel = RiskLevel.Medium
                }
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataPopulation,
                Action = "ExecutePopulateReferenceData",
                Detail = $"Populated {result.SuccessCount} reference data records in '{tableLogicalName}' ({result.FailureCount} failures); planId={planId}",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("ExecutePopulateReferenceData succeeded: planId={PlanId}, entitySetName={EntitySetName}, successCount={SuccessCount}, failureCount={FailureCount}",
                planId, entitySetName, result.SuccessCount, result.FailureCount);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecutePopulateReferenceData"), new KeyValuePair<string, object?>("tool.status", "success"));

            return result;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecutePopulateReferenceData failed: planId={PlanId}, entitySetName={EntitySetName}, tableLogicalName={TableLogicalName}",
                planId, entitySetName, tableLogicalName);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataPopulation,
                Action = "ExecutePopulateReferenceData",
                Detail = $"Failed to populate reference data in '{tableLogicalName}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecutePopulateReferenceData"), new KeyValuePair<string, object?>("tool.status", "error"));

            return new BatchOperationResult(0, recordPayloads.Count, Array.Empty<Guid>(), [ex.Message]);
        }
    }
}
