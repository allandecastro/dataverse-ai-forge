using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Removes a security role from a model-driven app. Direct (no plan/execute
/// split). Auto-records a planId in <c>state.ExecutedPlans</c> so the
/// Tester's <c>verify_app_role_assignment_against_plan</c> can confirm
/// the role is gone.
/// </summary>
public class UnassignAppRoleTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<UnassignAppRoleTool> logger)
{
    [Description("Remove a security role from a model-driven app. Idempotent — Dataverse returns "
        + "204 even when the role wasn't assigned in the first place. Returns a planId the "
        + "Tester can use with verify_app_role_assignment_against_plan.")]
    public async Task<AppRoleAssignmentResult> UnassignAppRoleAsync(
        [Description("appmoduleid (GUID) of the target app.")] string appId,
        [Description("roleid (GUID) of the security role to remove from the app.")] string roleId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(appId, out var parsedAppId) || parsedAppId == Guid.Empty)
            return new AppRoleAssignmentResult(string.Empty, false, "appId must be a valid GUID.");
        if (!Guid.TryParse(roleId, out var parsedRoleId) || parsedRoleId == Guid.Empty)
            return new AppRoleAssignmentResult(string.Empty, false, "roleId must be a valid GUID.");

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        var planId = $"unassign_app_role_{Guid.NewGuid():N}";

        try
        {
            await dataverseClient.UnassignAppRoleAsync(parsedAppId, parsedRoleId, cancellationToken);

            state.ExecutedPlans[planId] = JsonSerializer.Serialize(new
            {
                action = "unassign",
                appId = parsedAppId,
                roleId = parsedRoleId,
                timestamp = DateTime.UtcNow,
            });

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                ToolName = "UnassignAppRole",
                Parameters = new { appId = parsedAppId, roleId = parsedRoleId },
                Result = new { appId = parsedAppId, roleId = parsedRoleId },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "AssignAppRole",
                    Parameters = new { appId = parsedAppId, roleId = parsedRoleId },
                    Warning = "Re-grants the role to the app.",
                    RiskLevel = RiskLevel.Low,
                },
            });
            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "UnassignAppRole",
                Detail = $"Unassigned role {parsedRoleId} from app {parsedAppId} (planId={planId})",
                Success = true,
            });
            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            return new AppRoleAssignmentResult(planId, true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "UnassignAppRole failed: appId={AppId}, roleId={RoleId}", parsedAppId, parsedRoleId);
            return new AppRoleAssignmentResult(string.Empty, false, ex.Message);
        }
    }
}
