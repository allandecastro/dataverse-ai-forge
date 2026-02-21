using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateGlobalOptionSetTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Execute the creation of a global option set in Dataverse using a pre-built OData JSON payload "
        + "from plan_create_global_option_set. Creates the option set, adds it to the solution, updates project state, "
        + "and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateGlobalOptionSetAsync(
        [Description("The full OData JSON payload from plan_create_global_option_set")] string webApiPayloadJson,
        [Description("The schema name of the global option set")] string optionSetName,
        [Description("The solution unique name to add the option set to")] string solutionUniqueName,
        CancellationToken cancellationToken)
    {
        var metadataId = await dataverseClient.CreateGlobalOptionSetAsync(webApiPayloadJson, cancellationToken);

        // Add to solution (ComponentType 9 = OptionSet)
        await dataverseClient.AddSolutionComponentAsync(metadataId, 9, solutionUniqueName, cancellationToken);

        // Update project state
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        state.OptionSets.Add(new CreatedOptionSet(metadataId, optionSetName, IsGlobal: true, DateTime.UtcNow));

        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.DataModelBuilder,
            ToolName = "ExecuteCreateGlobalOptionSet",
            Parameters = new { optionSetName, solutionUniqueName },
            Result = new { metadataId },
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "DeleteGlobalOptionSet",
                Parameters = new { metadataId, optionSetName },
                Warning = "Deleting a global option set removes it from all fields that reference it.",
                RiskLevel = RiskLevel.High
            }
        });

        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return $"Global option set '{optionSetName}' created with MetadataId={metadataId} and added to solution '{solutionUniqueName}'.";
    }
}
