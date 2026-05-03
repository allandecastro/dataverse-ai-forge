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

public class ExecuteGenerateTestDataTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteGenerateTestDataTool> logger)
{
    [Description("Execute batch creation of test data records using the payload from plan_generate_test_data. "
        + "Creates records via individual POSTs (one per record), tags them as test data, updates project "
        + "state, and pushes to operation stack. recordPayloadsJson accepts ANY of: (a) plain JSON array of "
        + "record objects [{...},{...}], (b) JSON array of stringified records [\"{...}\",\"{...}\"], or "
        + "(c) the full WebApiBatchPayloadJson from the plan {\"entitySetName\":\"...\",\"records\":[...]} — "
        + "the tool normalizes all three shapes.")]
    public async Task<BatchOperationResult> ExecuteGenerateTestDataAsync(
        [Description("The entity set name for the OData endpoint (e.g., 'eppc_gifts')")] string entitySetName,
        [Description("The table logical name (e.g., 'eppc_gift')")] string tableLogicalName,
        [Description("Records to create. Easiest shape: pass the WebApiBatchPayloadJson string from the "
            + "plan response verbatim. Also accepts a plain JSON array of objects or strings.")]
        string recordPayloadsJson,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteGenerateTestData executing: entitySetName={EntitySetName}, tableLogicalName={TableLogicalName}",
            entitySetName, tableLogicalName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        // Tolerant input parsing. The agent has historically passed three shapes
        // here; deserializing one shape and crashing on the others surfaces as the
        // generic "Function failed." error from Microsoft.Extensions.AI's tool
        // dispatcher (since the JsonException escaped this method un-caught).
        // Accept all three shapes and convert any to List<string> of record JSON.
        List<string> recordPayloads;
        try
        {
            recordPayloads = NormalizeRecordPayloads(recordPayloadsJson);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "ExecuteGenerateTestData: recordPayloadsJson was not parseable JSON");
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteGenerateTestData"), new KeyValuePair<string, object?>("tool.status", "error"));
            return new BatchOperationResult(
                SuccessCount: 0,
                FailureCount: 0,
                CreatedRecordIds: Array.Empty<Guid>(),
                Errors: [$"recordPayloadsJson was not valid JSON: {ex.Message}. "
                    + "Pass the WebApiBatchPayloadJson value from plan_generate_test_data verbatim."]);
        }
        if (recordPayloads.Count == 0)
        {
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteGenerateTestData"), new KeyValuePair<string, object?>("tool.status", "error"));
            return new BatchOperationResult(
                SuccessCount: 0,
                FailureCount: 0,
                CreatedRecordIds: Array.Empty<Guid>(),
                Errors: ["No records found in recordPayloadsJson. The plan probably returned an empty "
                    + "records array — re-call plan_generate_test_data with concrete record values."]);
        }

        try
        {
            var result = await dataverseClient.CreateRecordsBatchAsync(entitySetName, recordPayloads.AsReadOnly(), cancellationToken);

            foreach (var recordId in result.CreatedRecordIds)
            {
                state.SeedDataRecords.Add(new CreatedRecord(recordId, tableLogicalName, "[TEST DATA]", DateTime.UtcNow));
            }

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.DataPopulation,
                ToolName = "ExecuteGenerateTestData",
                Parameters = new { entitySetName, tableLogicalName, recordCount = recordPayloads.Count },
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

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataPopulation,
                Action = "ExecuteGenerateTestData",
                Detail = $"Generated {result.SuccessCount} test data records in '{tableLogicalName}' ({result.FailureCount} failures)",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("ExecuteGenerateTestData succeeded: entitySetName={EntitySetName}, successCount={SuccessCount}, failureCount={FailureCount}",
                entitySetName, result.SuccessCount, result.FailureCount);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteGenerateTestData"), new KeyValuePair<string, object?>("tool.status", "success"));

            return result;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteGenerateTestData failed: entitySetName={EntitySetName}, tableLogicalName={TableLogicalName}",
                entitySetName, tableLogicalName);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataPopulation,
                Action = "ExecuteGenerateTestData",
                Detail = $"Failed to generate test data in '{tableLogicalName}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteGenerateTestData"), new KeyValuePair<string, object?>("tool.status", "error"));

            return new BatchOperationResult(0, recordPayloads.Count, Array.Empty<Guid>(), [ex.Message]);
        }
    }

    /// <summary>
    /// Normalizes any of the three shapes the agent has historically passed:
    ///   1. <c>["{json1}","{json2}"]</c> — array of stringified records (legacy).
    ///   2. <c>[{...},{...}]</c> — array of record objects (most natural).
    ///   3. <c>{"entitySetName":"...","records":[...]}</c> — full WebApiBatchPayloadJson
    ///      from plan_generate_test_data, where <c>records</c> is either of (1) or (2).
    /// Returns a list of single-record JSON strings ready for individual POSTs.
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
                    // Already a stringified record JSON — use as-is.
                    result.Add(el.GetString() ?? "");
                    break;
                case JsonValueKind.Object:
                    // Object literal — re-serialize to a string for the per-record POST.
                    result.Add(el.GetRawText());
                    break;
                // Skip null/number/etc — caller surfaces empty list as a clean error.
            }
        }
        return result;
    }
}
