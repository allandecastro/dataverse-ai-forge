using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ExecutePopulateReferenceDataTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Execute batch creation of reference/seed data records using the payload from plan_populate_reference_data. "
        + "Creates records via $batch, updates project state, and pushes to operation stack.")]
    public async Task<BatchOperationResult> ExecutePopulateReferenceDataAsync(
        [Description("The entity set name for the OData endpoint")] string entitySetName,
        [Description("The table logical name")] string tableLogicalName,
        [Description("JSON array of individual record payloads to create")] string recordPayloadsJson,
        CancellationToken cancellationToken)
    {
        var recordPayloads = JsonSerializer.Deserialize<List<string>>(recordPayloadsJson) ?? [];
        var result = await dataverseClient.CreateRecordsBatchAsync(entitySetName, recordPayloads.AsReadOnly(), cancellationToken);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

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

        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return result;
    }
}
