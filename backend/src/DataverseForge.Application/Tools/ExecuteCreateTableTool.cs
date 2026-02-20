using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateTableTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Execute the creation of a Dataverse table using a pre-built OData JSON payload from plan_create_table. "
        + "Creates the table, adds it to the solution, updates project state, and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateTableAsync(
        [Description("The full OData JSON payload from plan_create_table")] string webApiPayloadJson,
        [Description("The logical name of the table being created")] string tableLogicalName,
        [Description("The display name of the table")] string displayName,
        [Description("The solution unique name to add the table to")] string solutionUniqueName,
        CancellationToken cancellationToken)
    {
        var metadataId = await dataverseClient.CreateTableAsync(webApiPayloadJson, cancellationToken);

        // Add to solution (ComponentType 1 = Entity)
        await dataverseClient.AddSolutionComponentAsync(metadataId, 1, solutionUniqueName, cancellationToken);

        // Update project state
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        state.Tables.Add(new CreatedTable(metadataId, tableLogicalName, displayName, DateTime.UtcNow));

        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.DataModelBuilder,
            ToolName = "ExecuteCreateTable",
            Parameters = new { tableLogicalName, displayName, solutionUniqueName },
            Result = new { metadataId },
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "DeleteTable",
                Parameters = new { metadataId, tableLogicalName },
                Warning = "Deleting a table removes all data and dependent components.",
                RiskLevel = RiskLevel.High
            }
        });

        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return $"Table '{displayName}' ({tableLogicalName}) created successfully with MetadataId={metadataId} and added to solution '{solutionUniqueName}'.";
    }
}
