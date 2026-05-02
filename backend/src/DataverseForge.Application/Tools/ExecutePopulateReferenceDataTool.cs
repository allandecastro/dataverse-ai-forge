using DataverseForge.Application.Agents;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecutePopulateReferenceDataTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecutePopulateReferenceDataTool> logger)
{
    [Description("Execute batch creation of reference/seed data records using the payload from plan_populate_reference_data. "
        + "Creates records via $batch, updates project state, and pushes to operation stack.")]
    public async Task<BatchOperationResult> ExecutePopulateReferenceDataAsync(
        [Description("The entity set name for the OData endpoint")] string entitySetName,
        [Description("The table logical name")] string tableLogicalName,
        [Description("JSON array of individual record payloads to create")] string recordPayloadsJson,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecutePopulateReferenceData executing: entitySetName={EntitySetName}, tableLogicalName={TableLogicalName}",
            entitySetName, tableLogicalName);

        var recordPayloads = JsonSerializer.Deserialize<List<string>>(recordPayloadsJson) ?? [];
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            var result = await dataverseClient.CreateRecordsBatchAsync(entitySetName, recordPayloads.AsReadOnly(), cancellationToken);

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
                Parameters = new { entitySetName, tableLogicalName, recordCount = recordPayloads.Count },
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
                Detail = $"Populated {result.SuccessCount} reference data records in '{tableLogicalName}' ({result.FailureCount} failures)",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("ExecutePopulateReferenceData succeeded: entitySetName={EntitySetName}, successCount={SuccessCount}, failureCount={FailureCount}",
                entitySetName, result.SuccessCount, result.FailureCount);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecutePopulateReferenceData"), new KeyValuePair<string, object?>("tool.status", "success"));

            return result;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecutePopulateReferenceData failed: entitySetName={EntitySetName}, tableLogicalName={TableLogicalName}",
                entitySetName, tableLogicalName);

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
