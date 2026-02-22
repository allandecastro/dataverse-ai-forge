using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ExecuteGenerateTestDataTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Execute batch creation of test data records using the payload from plan_generate_test_data. "
        + "Creates records via $batch, tags them as test data, updates project state, and pushes to operation stack.")]
    public async Task<BatchOperationResult> ExecuteGenerateTestDataAsync(
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

        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return result;
    }
}
