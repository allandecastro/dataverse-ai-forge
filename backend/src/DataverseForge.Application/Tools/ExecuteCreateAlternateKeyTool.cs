using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Application.Agents;
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
        + "from plan_create_alternate_key. The target table is identified by its LOGICAL name — "
        + "never pass a tableMetadataId GUID.")]
    public async Task<string> ExecuteCreateAlternateKeyAsync(
        [Description("The logical name of the table (e.g. 'eppc_gift'). The MetadataId is resolved server-side.")] string tableLogicalName,
        [Description("The full OData JSON payload from plan_create_alternate_key")] string webApiPayloadJson,
        [Description("The logical name of the key")] string keyLogicalName,
        [Description("The display name of the key")] string keyDisplayName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteCreateAlternateKey executing: tableLogicalName={TableLogicalName}, keyLogicalName={KeyLogicalName}, keyDisplayName={KeyDisplayName}",
            tableLogicalName, keyLogicalName, keyDisplayName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            var metadataId = await dataverseClient.CreateAlternateKeyAsync(tableLogicalName, webApiPayloadJson, cancellationToken);

            state.AlternateKeys.Add(new CreatedAlternateKey(metadataId, tableLogicalName, keyLogicalName, keyDisplayName, DateTime.UtcNow));

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.DataModelBuilder,
                ToolName = "ExecuteCreateAlternateKey",
                Parameters = new { tableLogicalName, keyLogicalName, keyDisplayName },
                Result = new { metadataId },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "DeleteAlternateKey",
                    Parameters = new { tableLogicalName, metadataId, keyLogicalName },
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
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateAlternateKey"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"Alternate key '{keyDisplayName}' ({keyLogicalName.ToLowerInvariant()}) created on table '{tableLogicalName.ToLowerInvariant()}' with MetadataId={metadataId}. "
                + "NEXT REQUIRED ACTION: call handoff_to_tester so the Tester can verify the alternate key.";
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

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateAlternateKey"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to create alternate key '{keyDisplayName}' ({keyLogicalName}) on table '{tableLogicalName}': {ex.Message}. "
                + "Check that the table exists and the payload is valid. "
                + "You may retry or hand off to Solution Architect for diagnosis.";
        }
    }
}
