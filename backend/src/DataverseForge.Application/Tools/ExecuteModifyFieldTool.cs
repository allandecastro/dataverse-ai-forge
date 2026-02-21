using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ExecuteModifyFieldTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Execute the modification of an existing field on a Dataverse table using a pre-built OData JSON payload "
        + "from plan_modify_field. Updates the field metadata, updates project state, "
        + "and pushes to the operation stack.")]
    public async Task<string> ExecuteModifyFieldAsync(
        [Description("The GUID of the table's MetadataId")] string tableMetadataId,
        [Description("The GUID of the field's MetadataId")] string fieldMetadataId,
        [Description("The logical name of the table")] string tableLogicalName,
        [Description("The logical name of the field")] string fieldLogicalName,
        [Description("The full OData JSON payload from plan_modify_field")] string webApiPayloadJson,
        [Description("Summary of what was changed (e.g., 'displayName, requiredLevel')")] string changeSummary,
        CancellationToken cancellationToken)
    {
        var tableId = Guid.Parse(tableMetadataId);
        var fieldId = Guid.Parse(fieldMetadataId);

        await dataverseClient.ModifyFieldAsync(tableId, fieldId, webApiPayloadJson, cancellationToken);

        // Update project state
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.DataModelBuilder,
            ToolName = "ExecuteModifyField",
            Parameters = new { tableMetadataId, fieldMetadataId, tableLogicalName, fieldLogicalName, changeSummary },
            Result = new { fieldMetadataId, changeSummary },
            IsReversible = false,
            ReverseOp = null
        });

        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return $"Field '{fieldLogicalName}' on table '{tableLogicalName}' modified successfully. Changes: {changeSummary}.";
    }
}
