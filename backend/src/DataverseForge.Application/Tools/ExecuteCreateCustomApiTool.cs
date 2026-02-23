using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateCustomApiTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateCustomApiTool> logger)
{
    [Description("Execute the creation of a Dataverse Custom API using a pre-built OData JSON payload from plan_create_custom_api. "
        + "Creates the Custom API, updates project state, and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateCustomApiAsync(
        [Description("The full OData JSON payload from plan_create_custom_api")] string webApiPayloadJson,
        [Description("The unique name of the Custom API")] string uniqueName,
        [Description("The display name of the Custom API")] string displayName,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("ExecuteCreateCustomApi executing: uniqueName={UniqueName}, displayName={DisplayName}", uniqueName, displayName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            var customApiId = await dataverseClient.CreateCustomApiAsync(webApiPayloadJson, cancellationToken);

            // Custom APIs are tracked as plugins in project state (they're backed by plugin types)
            state.Plugins.Add(new CreatedPlugin(customApiId, displayName, string.Empty, "CustomApi", 0, DateTime.UtcNow));

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.AutomationBuilder,
                ToolName = "ExecuteCreateCustomApi",
                Parameters = new { uniqueName, displayName },
                Result = new { customApiId },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "DeleteCustomApi",
                    Parameters = new { customApiId, uniqueName },
                    Warning = "Deleting a Custom API removes the endpoint and breaks any integrations using it.",
                    RiskLevel = RiskLevel.High
                }
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.AutomationBuilder,
                Action = "ExecuteCreateCustomApi",
                Detail = $"Created Custom API '{displayName}' ({uniqueName}) with CustomApiId={customApiId}",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("ExecuteCreateCustomApi succeeded: customApiId={CustomApiId}", customApiId);

            return $"Custom API '{displayName}' ({uniqueName}) created with CustomApiId={customApiId}.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteCreateCustomApi failed: uniqueName={UniqueName}, displayName={DisplayName}", uniqueName, displayName);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.AutomationBuilder,
                Action = "ExecuteCreateCustomApi",
                Detail = $"Failed to create Custom API '{displayName}' ({uniqueName}): {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            return $"ERROR: Failed to create Custom API '{displayName}' ({uniqueName}). {ex.Message}. Check connectivity and payload format.";
        }
    }
}
