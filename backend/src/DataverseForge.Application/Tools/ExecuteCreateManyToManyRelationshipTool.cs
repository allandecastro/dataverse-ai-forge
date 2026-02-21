using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateManyToManyRelationshipTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Execute the creation of an N:N (many-to-many) relationship between two Dataverse tables using a pre-built OData JSON payload "
        + "from plan_create_many_to_many_relationship. Creates the relationship, adds it to the solution, updates project state, "
        + "and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateManyToManyRelationshipAsync(
        [Description("The full OData JSON payload from plan_create_many_to_many_relationship")] string webApiPayloadJson,
        [Description("The schema name of the relationship")] string relationshipSchemaName,
        [Description("The logical name of the first table")] string firstTable,
        [Description("The logical name of the second table")] string secondTable,
        [Description("The name of the intersect table")] string intersectTableName,
        [Description("The solution unique name to add the relationship to")] string solutionUniqueName,
        CancellationToken cancellationToken)
    {
        var metadataId = await dataverseClient.CreateManyToManyRelationshipAsync(webApiPayloadJson, cancellationToken);

        // Add to solution (ComponentType 10 = EntityRelationship)
        await dataverseClient.AddSolutionComponentAsync(metadataId, 10, solutionUniqueName, cancellationToken);

        // Update project state
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        state.Relationships.Add(new CreatedRelationship(metadataId, relationshipSchemaName, firstTable, secondTable, "ManyToMany", DateTime.UtcNow));

        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.DataModelBuilder,
            ToolName = "ExecuteCreateManyToManyRelationship",
            Parameters = new { relationshipSchemaName, firstTable, secondTable, intersectTableName, solutionUniqueName },
            Result = new { metadataId },
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "DeleteRelationship",
                Parameters = new { metadataId, relationshipSchemaName },
                Warning = "Deleting an N:N relationship removes the intersect table and all association data.",
                RiskLevel = RiskLevel.High
            }
        });

        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return $"N:N relationship '{relationshipSchemaName}' ({firstTable} <-> {secondTable}) created with MetadataId={metadataId}, intersect table '{intersectTableName}', and added to solution '{solutionUniqueName}'.";
    }
}
