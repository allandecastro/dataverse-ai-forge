using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateViewTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateViewTool> logger)
{
    [Description("Execute the creation of a Dataverse system view using a pre-built OData JSON payload from plan_create_view. "
        + "Creates the view, updates project state, and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateViewAsync(
        [Description("The full OData JSON payload from plan_create_view")] string webApiPayloadJson,
        [Description("The logical name of the table the view belongs to")] string tableLogicalName,
        [Description("The display name of the view")] string viewName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteCreateView executing: tableLogicalName={TableLogicalName}, viewName={ViewName}",
            tableLogicalName, viewName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            var savedQueryId = await dataverseClient.CreateViewAsync(webApiPayloadJson, cancellationToken);

            state.Views.Add(new CreatedView(savedQueryId, tableLogicalName, viewName, DateTime.UtcNow));

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                ToolName = "ExecuteCreateView",
                Parameters = new { tableLogicalName, viewName },
                Result = new { savedQueryId },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "DeleteView",
                    Parameters = new { savedQueryId, tableLogicalName },
                    Warning = "Deleting a view removes it from all model-driven apps that reference it.",
                    RiskLevel = RiskLevel.Medium
                }
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "ExecuteCreateView",
                Detail = $"Created view '{viewName}' on table '{tableLogicalName}'",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("ExecuteCreateView succeeded: savedQueryId={SavedQueryId}, viewName={ViewName}", savedQueryId, viewName);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateView"), new KeyValuePair<string, object?>("tool.status", "success"));
            return $"View '{viewName}' created on table '{tableLogicalName}' with SavedQueryId={savedQueryId}.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteCreateView failed: tableLogicalName={TableLogicalName}, viewName={ViewName}", tableLogicalName, viewName);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "ExecuteCreateView",
                Detail = $"Failed to create view '{viewName}' on table '{tableLogicalName}': {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateView"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Failed to create view '{viewName}' on table '{tableLogicalName}': {ex.Message}. "
                + "Check that the table exists and the payload is valid. "
                + "You may retry or hand off to Solution Architect for diagnosis.";
        }
    }
}
