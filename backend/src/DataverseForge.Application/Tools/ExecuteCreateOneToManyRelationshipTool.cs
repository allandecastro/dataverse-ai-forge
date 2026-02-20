using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateOneToManyRelationshipTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Execute the creation of a 1:N relationship between two Dataverse tables using a pre-built OData JSON payload "
        + "from plan_create_one_to_many_relationship. Creates the relationship, adds it to the solution, updates project state, "
        + "and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateOneToManyRelationshipAsync(
        [Description("The full OData JSON payload from plan_create_one_to_many_relationship")] string webApiPayloadJson,
        [Description("The schema name of the relationship")] string relationshipSchemaName,
        [Description("The logical name of the primary (one-side) table")] string primaryTable,
        [Description("The logical name of the related (many-side) table")] string relatedTable,
        [Description("The solution unique name to add the relationship to")] string solutionUniqueName,
        CancellationToken cancellationToken)
    {
        var metadataId = await dataverseClient.CreateOneToManyRelationshipAsync(webApiPayloadJson, cancellationToken);

        // Add to solution (ComponentType 10 = EntityRelationship)
        await dataverseClient.AddSolutionComponentAsync(metadataId, 10, solutionUniqueName, cancellationToken);

        // Update project state
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        state.Relationships.Add(new CreatedRelationship(metadataId, relationshipSchemaName, primaryTable, relatedTable, "OneToMany", DateTime.UtcNow));

        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.DataModelBuilder,
            ToolName = "ExecuteCreateOneToManyRelationship",
            Parameters = new { relationshipSchemaName, primaryTable, relatedTable, solutionUniqueName },
            Result = new { metadataId },
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "DeleteRelationship",
                Parameters = new { metadataId, relationshipSchemaName },
                Warning = "Deleting a relationship removes the lookup field and all relationship data.",
                RiskLevel = RiskLevel.High
            }
        });

        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return $"1:N relationship '{relationshipSchemaName}' ({primaryTable} → {relatedTable}) created with MetadataId={metadataId} and added to solution '{solutionUniqueName}'.";
    }
}
