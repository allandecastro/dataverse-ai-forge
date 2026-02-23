using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateAlternateKeyTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateAlternateKeyTool> logger)
{
    [Description("Execute the creation of an alternate key on a Dataverse table using a pre-built OData JSON payload "
        + "from plan_create_alternate_key. Creates the key, updates project state, "
        + "and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateAlternateKeyAsync(
        [Description("The GUID of the table's MetadataId")] string tableMetadataId,
        [Description("The logical name of the table")] string tableLogicalName,
        [Description("The full OData JSON payload from plan_create_alternate_key")] string webApiPayloadJson,
        [Description("The logical name of the key")] string keyLogicalName,
        [Description("The display name of the key")] string keyDisplayName,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("ExecuteCreateAlternateKey executing: tableLogicalName={TableLogicalName}, keyLogicalName={KeyLogicalName}, keyDisplayName={KeyDisplayName}",
            tableLogicalName, keyLogicalName, keyDisplayName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            var tableId = Guid.Parse(tableMetadataId);
            var metadataId = await dataverseClient.CreateAlternateKeyAsync(tableId, webApiPayloadJson, cancellationToken);

            state.AlternateKeys.Add(new CreatedAlternateKey(metadataId, tableLogicalName, keyLogicalName, keyDisplayName, DateTime.UtcNow));

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                ToolName = "ExecuteCreateAlternateKey",
                Parameters = new { tableMetadataId, tableLogicalName, keyLogicalName, keyDisplayName },
                Result = new { metadataId },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "DeleteAlternateKey",
                    Parameters = new { tableMetadataId, metadataId, keyLogicalName },
                    Warning = "Deleting an alternate key removes the uniqueness constraint. Upsert operations referencing this key will fail.",
                    RiskLevel = RiskLevel.Medium
                }
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "ExecuteCreateAlternateKey",
                Detail = $"Created alternate key '{keyDisplayName}' ({keyLogicalName}) on table '{tableLogicalName}'",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("ExecuteCreateAlternateKey succeeded: metadataId={MetadataId}, keyLogicalName={KeyLogicalName}", metadataId, keyLogicalName);
            return $"Alternate key '{keyDisplayName}' ({keyLogicalName}) created on table '{tableLogicalName}' with MetadataId={metadataId}.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteCreateAlternateKey failed: tableLogicalName={TableLogicalName}, keyLogicalName={KeyLogicalName}", tableLogicalName, keyLogicalName);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                Action = "ExecuteCreateAlternateKey",
                Detail = $"Failed to create alternate key '{keyLogicalName}' on table '{tableLogicalName}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            return $"ERROR: Failed to create alternate key '{keyDisplayName}' ({keyLogicalName}) on table '{tableLogicalName}': {ex.Message}. "
                + "Check that the table exists and the payload is valid. "
                + "You may retry or hand off to Solution Architect for diagnosis.";
        }
    }
}
