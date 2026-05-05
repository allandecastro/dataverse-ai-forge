using System.ComponentModel;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class DeleteAppTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<DeleteAppTool> logger)
{
    [Description("Delete a model-driven app by appmoduleid. DESTRUCTIVE — request_approval BEFORE "
        + "calling. Use get_apps_list to find the appId. Cascades to the app's component "
        + "associations and role assignments; the referenced tables/forms/views remain. "
        + "Managed apps cannot be deleted (Dataverse rejects with HTTP 403).")]
    public async Task<string> DeleteAppAsync(
        [Description("The GUID of the app to delete (appmoduleid from get_apps_list)")] string appId,
        [Description("Display name (for audit logging)")] string name,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(appId, out var parsedId) || parsedId == Guid.Empty)
        {
            return "ERROR: appId must be a valid GUID — call get_apps_list to retrieve it.";
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            await dataverseClient.DeleteAppAsync(parsedId, cancellationToken);

            var existing = state.Apps.FirstOrDefault(a => a.AppModuleId == parsedId);
            if (existing is not null) state.Apps.Remove(existing);

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                ToolName = "DeleteApp",
                Parameters = new { appId = parsedId, name },
                Result = new { appId = parsedId },
                IsReversible = false,
                ReverseOp = null
            });

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "DeleteApp",
                Detail = $"Deleted app '{name}' (id={parsedId})",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
            return $"App '{name}' (id={parsedId}) deleted.";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "DeleteApp failed: id={Id}, name={Name}", parsedId, name);
            return $"ERROR: Failed to delete app '{name}' (id={parsedId}): {ex.Message}. "
                + "Common cause: the app is part of a managed solution.";
        }
    }
}
