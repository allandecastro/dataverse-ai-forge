using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class DeleteSecurityRoleTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<DeleteSecurityRoleTool> logger)
{
    [Description("Delete a Dataverse security role. DIRECT call — there's no plan/execute split "
        + "(matches delete_field, delete_alternate_key). REFUSES on: (1) managed roles, (2) "
        + "auto-assigned / system-defined roles, (3) roles with any user or team still holding "
        + "them. The pre-flight count comes from systemuserroles + teamroles — when non-zero, "
        + "the tool returns a clear error with the count and a maker-portal pointer; the user "
        + "must reassign first. Successful delete pushes an OperationStack entry that is NOT "
        + "reversible: undoing creates a role with the same NAME but a FRESH GUID and ZERO "
        + "privileges (the recreate doesn't restore the privilege matrix).")]
    public async Task<string> DeleteSecurityRoleAsync(
        [Description("RoleId (GUID) of the role to delete — from get_security_roles")] Guid roleId,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("DeleteSecurityRole: roleId={RoleId}", roleId);

        if (roleId == Guid.Empty)
        {
            return "ERROR: RoleId is required. Call get_security_roles first.";
        }

        // The AIFunctionFactory wrapper swallows any unhandled exception as
        // "Function failed." — useless for diagnosis. Wrap the whole body so
        // pre-flight read failures (GetSecurityRoleAsync, CountUsersAndTeams)
        // surface the real Dataverse error instead.
        try
        {

        var role = await dataverseClient.GetSecurityRoleAsync(roleId, cancellationToken);
        if (role is null)
        {
            logger.LogInformation("DeleteSecurityRole: role {RoleId} not found — treating as no-op", roleId);
            return $"NO_OP: Role {roleId} not found in this environment (already deleted or "
                + "wrong RoleId). Nothing to do.";
        }

        if (role.Info.IsManaged)
        {
            return $"REFUSED: Role '{role.Info.Name}' is managed (shipped via a managed "
                + "solution). Managed roles can't be deleted via the Web API — direct the user "
                + "to remove the managed solution itself, or to the maker portal.";
        }
        if (role.Info.IsAutoAssigned)
        {
            return $"REFUSED: Role '{role.Info.Name}' is auto-assigned by the platform. "
                + "Auto-assigned roles can't be deleted — they're managed by Dataverse.";
        }

        var inUse = await dataverseClient.CountUsersAndTeamsForRoleAsync(roleId, cancellationToken);
        if (inUse > 0)
        {
            logger.LogInformation(
                "DeleteSecurityRole: role {RoleId} has {Count} active assignments — refusing", roleId, inUse);
            return $"REFUSED: Role '{role.Info.Name}' has {inUse} active user/team "
                + "assignment(s). Removing the role would orphan those principals' access. "
                + "Reassign them first in the Power Platform admin centre "
                + "(https://admin.powerplatform.microsoft.com/) or via the maker portal, "
                + "then call delete_security_role again.";
        }

        try
        {
            await dataverseClient.DeleteRoleAsync(roleId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DeleteSecurityRole: DELETE failed for role {RoleId}", roleId);
            await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
            {
                fresh.AuditLog.Add(AuditEntry.Failed(AgentNames.SecurityBuilder, "DeleteSecurityRole", $"Failed to delete role '{role.Info.Name}' ({roleId}): {ex.Message}"));
            }, cancellationToken);
            sw.Stop();
            ForgeActivitySource.RecordToolDuration("DeleteSecurityRole", "error", sw.ElapsedMilliseconds);
            return $"ERROR: Failed to delete role '{role.Info.Name}': {ex.Message}";
        }

        await projectStateStore.ApplyAndSaveAsync(sessionContext.SessionId, fresh =>
        {
            fresh.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.SecurityBuilder,
                ToolName = "DeleteSecurityRole",
                Parameters = new { roleId, roleName = role.Info.Name },
                Result = new { roleId, roleName = role.Info.Name },
                IsReversible = false,
                ReverseOp = null,
            });
            fresh.AuditLog.Add(AuditEntry.Ok(AgentNames.SecurityBuilder, "DeleteSecurityRole", $"Deleted role '{role.Info.Name}' ({roleId})"));
        }, cancellationToken);

        sw.Stop();
        ForgeActivitySource.RecordToolDuration("DeleteSecurityRole", "success", sw.ElapsedMilliseconds);

        return $"Role '{role.Info.Name}' ({roleId}) deleted. NEXT REQUIRED ACTION: report to the "
            + "user that the role is gone and end the turn — there is nothing for the Tester to "
            + "verify on a delete.";

        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "DeleteSecurityRole: unhandled pre-flight failure for role {RoleId}", roleId);
            return $"ERROR: Delete pre-flight failed for role {roleId}: {ex.Message}. "
                + "Verify the role exists and you have permission to manage security roles, "
                + "or remove the role manually in the maker portal.";
        }
    }
}
