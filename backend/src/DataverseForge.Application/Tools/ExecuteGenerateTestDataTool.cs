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
/// Executes a test-data batch planned by <see cref="PlanGenerateTestDataTool"/>.
/// Uses the planId-by-reference pattern (see CLAUDE.md recurring bug #1):
/// the agent passes only the planId, and the tool looks up the
/// <c>WebApiBatchPayloadJson</c> from <c>state.PendingPlans</c>. Past failure
/// (real session) — agent passed the literal multi-KB payload string back to
/// this tool, mangled it during JSON serialization, and Dataverse rejected
/// with HTTP 400 "Stream was not readable". planId-by-reference makes that
/// failure mode impossible.
/// </summary>
public class ExecuteGenerateTestDataTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteGenerateTestDataTool> logger)
{
    [Description("Execute batch creation of test data records using the planId from "
        + "plan_generate_test_data — the OData payload (with the records array) was stored "
        + "server-side and will be loaded automatically. Do NOT construct or pass the payload "
        + "yourself. Creates records via individual POSTs (one per record), tags them as test "
        + "data, updates project state, and pushes to operation stack.")]
    public async Task<BatchOperationResult> ExecuteGenerateTestDataAsync(
        [Description("planId from plan_generate_test_data — must match exactly. The "
            + "WebApiBatchPayloadJson is stored server-side under this id; do NOT construct "
            + "the payload yourself.")]
        string planId,
        [Description("The entity set name for the OData endpoint (e.g., 'eppc_gifts'). Must "
            + "match what the plan stored — surfaced in the audit log.")]
        string entitySetName,
        [Description("The table logical name (e.g., 'eppc_gift'). Must match what the plan "
            + "stored — surfaced in the audit log.")]
        string tableLogicalName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteGenerateTestData executing: planId={PlanId}, entitySetName={EntitySetName}, tableLogicalName={TableLogicalName}",
            planId, entitySetName, tableLogicalName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (planId.StartsWith("rejected_", StringComparison.Ordinal))
        {
            return new BatchOperationResult(
                SuccessCount: 0,
                FailureCount: 0,
                CreatedRecordIds: Array.Empty<Guid>(),
                Errors: [$"SKIPPED: Plan '{planId}' was rejected by plan_generate_test_data. "
                    + "Fix the records and re-plan; do NOT execute this planId."]);
        }

        var lookupStatus = state.LookupPlan(planId, out var webApiBatchPayloadJson);
        if (lookupStatus == PlanLookupStatus.AlreadyExecuted)
        {
            logger.LogInformation("ExecuteGenerateTestData: planId {PlanId} already executed earlier this session", planId);
            return new BatchOperationResult(
                SuccessCount: 0,
                FailureCount: 0,
                CreatedRecordIds: Array.Empty<Guid>(),
                Errors: [$"ALREADY_EXECUTED: Plan '{planId}' was already executed earlier this session. "
                    + $"Records were inserted into '{entitySetName}'. DO NOT retry, DO NOT re-plan."]);
        }
        if (lookupStatus == PlanLookupStatus.NotFound)
        {
            logger.LogWarning("ExecuteGenerateTestData: planId not found in PendingPlans: {PlanId}", planId);
            return new BatchOperationResult(
                SuccessCount: 0,
                FailureCount: 0,
                CreatedRecordIds: Array.Empty<Guid>(),
                Errors: [$"ERROR: Plan '{planId}' not found. Call plan_generate_test_data first and "
                    + "pass its returned planId verbatim."]);
        }

        // The stored payload is the same WebApiBatchPayloadJson shape the plan
        // returned: { "entitySetName": "...", "records": [...] }. Reuse the
        // existing normalizer so we accept all three historic shapes — back-
        // compat for any plan still in PendingPlans from before this migration.
        List<string> recordPayloads;
        try
        {
            recordPayloads = NormalizeRecordPayloads(webApiBatchPayloadJson);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "ExecuteGenerateTestData: stored payload was not parseable JSON for plan {PlanId}", planId);
            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ExecuteGenerateTestData", "error", sw.ElapsedMilliseconds);
            return new BatchOperationResult(
                SuccessCount: 0,
                FailureCount: 0,
                CreatedRecordIds: Array.Empty<Guid>(),
                Errors: [$"Internal: stored payload for plan '{planId}' was not parseable JSON: {ex.Message}. "
                    + "Re-call plan_generate_test_data."]);
        }
        if (recordPayloads.Count == 0)
        {
            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ExecuteGenerateTestData", "error", sw.ElapsedMilliseconds);
            return new BatchOperationResult(
                SuccessCount: 0,
                FailureCount: 0,
                CreatedRecordIds: Array.Empty<Guid>(),
                Errors: [$"Plan '{planId}' has no records to insert. Re-call plan_generate_test_data "
                    + "with concrete record values."]);
        }

        try
        {
            var result = await dataverseClient.CreateRecordsBatchAsync(entitySetName, recordPayloads.AsReadOnly(), cancellationToken);

            var recordCount = recordPayloads.Count;
            // Mark the plan executed so a retry doesn't double-create.
            // F-23: re-read state right before mutate+save.
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.ExecutedPlans[planId] = webApiBatchPayloadJson;
                fresh.PendingPlans.Remove(planId);
                fresh.PendingPlanTimestamps.Remove(planId);

                foreach (var recordId in result.CreatedRecordIds)
                {
                    fresh.SeedDataRecords.Add(new CreatedRecord(recordId, tableLogicalName, "[TEST DATA]", DateTime.UtcNow));
                }

                fresh.OperationStack.Push(new ExecutedOperation
                {
                    OperationId = Guid.NewGuid().ToString("N"),
                    ExecutedAt = DateTime.UtcNow,
                    AgentName = AgentNames.DataPopulation,
                    ToolName = "ExecuteGenerateTestData",
                    Parameters = new { planId, entitySetName, tableLogicalName, recordCount },
                    Result = new { result.SuccessCount, result.FailureCount, recordIds = result.CreatedRecordIds },
                    IsReversible = true,
                    ReverseOp = new ReverseOperation
                    {
                        ToolName = "DeleteRecords",
                        Parameters = new { entitySetName, recordIds = result.CreatedRecordIds },
                        Warning = $"Deleting {result.SuccessCount} test data records from {tableLogicalName}.",
                        RiskLevel = RiskLevel.Medium
                    }
                });

                fresh.AuditLog.Add(AuditEntry.Ok(AgentNames.DataPopulation, "ExecuteGenerateTestData", $"Generated {result.SuccessCount} test data records in '{tableLogicalName}' ({result.FailureCount} failures); planId={planId}"));
            }, cancellationToken);

            logger.LogInformation("ExecuteGenerateTestData succeeded: planId={PlanId}, entitySetName={EntitySetName}, successCount={SuccessCount}, failureCount={FailureCount}",
                planId, entitySetName, result.SuccessCount, result.FailureCount);

            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ExecuteGenerateTestData", "success", sw.ElapsedMilliseconds);

            return result;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteGenerateTestData failed: planId={PlanId}, entitySetName={EntitySetName}, tableLogicalName={TableLogicalName}",
                planId, entitySetName, tableLogicalName);

            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(AuditEntry.Failed(AgentNames.DataPopulation, "ExecuteGenerateTestData", $"Failed to generate test data in '{tableLogicalName}': {ex.Message}"));
            }, cancellationToken);

            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ExecuteGenerateTestData", "error", sw.ElapsedMilliseconds);

            return new BatchOperationResult(0, recordPayloads.Count, Array.Empty<Guid>(), [ex.Message]);
        }
    }

    /// <summary>
    /// Normalizes any of the three shapes the plan tool may have stored:
    ///   1. <c>["{json1}","{json2}"]</c> — array of stringified records (legacy).
    ///   2. <c>[{...},{...}]</c> — array of record objects.
    ///   3. <c>{"entitySetName":"...","records":[...]}</c> — full WebApiBatchPayloadJson
    ///      from plan_generate_test_data, where <c>records</c> is either of (1) or (2).
    /// Kept tolerant so any old PendingPlans entry from before the planId-by-
    /// reference migration still executes cleanly.
    /// </summary>
    private static List<string> NormalizeRecordPayloads(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        // Shape 3: object with "records" property
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("records", out var recordsEl))
        {
            return ExtractFromArray(recordsEl);
        }

        // Shapes 1 + 2: array
        if (root.ValueKind == JsonValueKind.Array)
        {
            return ExtractFromArray(root);
        }

        return [];
    }

    private static List<string> ExtractFromArray(JsonElement arr)
    {
        var result = new List<string>(arr.GetArrayLength());
        foreach (var el in arr.EnumerateArray())
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.String:
                    result.Add(el.GetString() ?? "");
                    break;
                case JsonValueKind.Object:
                    result.Add(el.GetRawText());
                    break;
            }
        }
        return result;
    }
}
