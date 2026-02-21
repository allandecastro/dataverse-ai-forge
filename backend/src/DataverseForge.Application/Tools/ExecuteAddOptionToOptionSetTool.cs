using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ExecuteAddOptionToOptionSetTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Execute the addition of a new option value to an existing Dataverse option set using a pre-built OData JSON payload "
        + "from plan_add_option_to_option_set. Inserts the option value, updates project state, "
        + "and pushes to the operation stack.")]
    public async Task<string> ExecuteAddOptionToOptionSetAsync(
        [Description("The full OData JSON payload from plan_add_option_to_option_set")] string webApiPayloadJson,
        [Description("The name of the option set")] string optionSetName,
        [Description("True for global option set, false for local")] bool isGlobal,
        [Description("The integer value of the new option")] int newOptionValue,
        [Description("The display label of the new option")] string newOptionLabel,
        CancellationToken cancellationToken)
    {
        await dataverseClient.InsertOptionValueAsync(webApiPayloadJson, cancellationToken);

        // Update project state
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.DataModelBuilder,
            ToolName = "ExecuteAddOptionToOptionSet",
            Parameters = new { optionSetName, isGlobal, newOptionValue, newOptionLabel },
            Result = new { newOptionValue },
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "DeleteOptionValue",
                Parameters = new { optionSetName, newOptionValue },
                Warning = "Deleting an option value may cause data loss if existing records use this value.",
                RiskLevel = RiskLevel.Medium
            }
        });

        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        var scope = isGlobal ? $"global option set '{optionSetName}'" : $"local option set '{optionSetName}'";
        return $"Option value {newOptionValue} ('{newOptionLabel}') added to {scope}.";
    }
}
