using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class ExecuteCreateModelDrivenAppTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<ExecuteCreateModelDrivenAppTool> logger)
{
    [Description("Execute the creation of a Dataverse model-driven app using a pre-built OData JSON payload from plan_create_model_driven_app. "
        + "Creates the app, updates project state, and pushes to the operation stack.")]
    public async Task<string> ExecuteCreateModelDrivenAppAsync(
        [Description("The full OData JSON payload from plan_create_model_driven_app")] string webApiPayloadJson,
        [Description("The display name of the app")] string appName,
        [Description("The unique name of the app")] string appUniqueName,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("ExecuteCreateModelDrivenApp executing: appName={AppName}, appUniqueName={AppUniqueName}", appName, appUniqueName);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            var appId = await dataverseClient.CreateModelDrivenAppAsync(webApiPayloadJson, cancellationToken);

            state.ModelDrivenApps.Add(new CreatedModelDrivenApp(appId, appUniqueName, appName, DateTime.UtcNow));

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                ToolName = "ExecuteCreateModelDrivenApp",
                Parameters = new { appName, appUniqueName },
                Result = new { appId },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "DeleteModelDrivenApp",
                    Parameters = new { appId, appUniqueName },
                    Warning = "Deleting a model-driven app removes it from the app launcher for all users.",
                    RiskLevel = RiskLevel.High
                }
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "ExecuteCreateModelDrivenApp",
                Detail = $"Created model-driven app '{appName}' ({appUniqueName}) with AppId={appId}",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("ExecuteCreateModelDrivenApp succeeded: appId={AppId}", appId);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateModelDrivenApp"), new KeyValuePair<string, object?>("tool.status", "success"));

            return $"Model-driven app '{appName}' ({appUniqueName}) created with AppId={appId}.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "ExecuteCreateModelDrivenApp failed: appName={AppName}, appUniqueName={AppUniqueName}", appName, appUniqueName);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "ExecuteCreateModelDrivenApp",
                Detail = $"Failed to create model-driven app '{appName}' ({appUniqueName}): {ex.Message}",
                Success = false
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExecuteCreateModelDrivenApp"), new KeyValuePair<string, object?>("tool.status", "error"));

            return $"ERROR: Failed to create model-driven app '{appName}' ({appUniqueName}). {ex.Message}. Check connectivity and payload format.";
        }
    }
}
