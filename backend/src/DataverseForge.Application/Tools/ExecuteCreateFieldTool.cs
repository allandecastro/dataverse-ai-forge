using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateFieldTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Execute the creation of a field (column) on a Dataverse table using a pre-built OData JSON payload from plan_create_field. "
        + "Creates the field, updates project state, and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateFieldAsync(
        [Description("The MetadataId (GUID) of the target table")] string tableMetadataId,
        [Description("The logical name of the target table")] string tableLogicalName,
        [Description("The full OData JSON payload from plan_create_field")] string webApiPayloadJson,
        [Description("The logical name of the field being created")] string fieldLogicalName,
        [Description("The display name of the field")] string displayName,
        [Description("The field type (e.g., 'string', 'integer', 'boolean')")] string fieldType,
        CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            var metadataId = await dataverseClient.CreateFieldAsync(Guid.Parse(tableMetadataId), webApiPayloadJson, cancellationToken);

            state.Fields.Add(new CreatedField(metadataId, tableLogicalName, fieldLogicalName, displayName, fieldType, DateTime.UtcNow));

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                ToolName = "ExecuteCreateField",
                Parameters = new { tableLogicalName, fieldLogicalName, displayName, fieldType },
                Result = new { metadataId },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "DeleteField",
                    Parameters = new { tableMetadataId = Guid.Parse(tableMetadataId), fieldMetadataId = metadataId, fieldLogicalName },
                    Warning = "Deleting a field removes all data stored in it.",
                    RiskLevel = RiskLevel.High
                }
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "ExecuteCreateField",
                Detail = $"Created field '{displayName}' ({fieldLogicalName}) on table '{tableLogicalName}'",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            return $"Field '{displayName}' ({fieldLogicalName}) of type '{fieldType}' created on table '{tableLogicalName}' with MetadataId={metadataId}.";
        }
        catch (HttpRequestException ex)
        {
            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "ExecuteCreateField",
                Detail = $"Failed to create field '{fieldLogicalName}' on '{tableLogicalName}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            return $"ERROR: Failed to create field '{displayName}' ({fieldLogicalName}): {ex.Message}. "
                + "Check that the table exists and the payload is valid. "
                + "You may retry or hand off to Solution Architect for diagnosis.";
        }
    }
}
