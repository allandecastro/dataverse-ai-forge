using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Assigns a security role to a model-driven app via the
/// <c>appmoduleroles_association</c> M:N navigation. Direct (no plan/execute
/// split) — the M:N association is idempotent and low-risk, matching the
/// <c>attach_sitemap_to_app</c> precedent.
///
/// Auto-records a planId in <c>state.ExecutedPlans</c> so the Tester's
/// <c>verify_app_role_assignment_against_plan</c> tool has something to diff
/// against. Returns the planId so the agent can pass it to the verifier.
/// </summary>
public class AssignAppRoleTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<AssignAppRoleTool> logger)
{
    [Description("Assign a security role to a model-driven app. Pass the appId (GUID) and the "
        + "roleId (GUID — get from get_security_roles by name). Direct write — Dataverse "
        + "treats re-assigning an already-linked role as an idempotent no-op. Returns a "
        + "planId the Tester can use with verify_app_role_assignment_against_plan.")]
    public async Task<AppRoleAssignmentResult> AssignAppRoleAsync(
        [Description("appmoduleid (GUID) of the target app.")] string appId,
        [Description("roleid (GUID) of the security role to grant access to the app.")] string roleId,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        if (!Guid.TryParse(appId, out var parsedAppId) || parsedAppId == Guid.Empty)
            return new AppRoleAssignmentResult(string.Empty, false, "appId must be a valid GUID.");
        if (!Guid.TryParse(roleId, out var parsedRoleId) || parsedRoleId == Guid.Empty)
            return new AppRoleAssignmentResult(string.Empty, false, "roleId must be a valid GUID.");

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        var planId = $"assign_app_role_{Guid.NewGuid():N}";

        try
        {
            await dataverseClient.AssignAppRoleAsync(parsedAppId, parsedRoleId, cancellationToken);

            // Record the assignment in ExecutedPlans so the Tester's
            // verify_app_role_assignment_against_plan tool can read it.
            // Plain JSON payload mirrors the shape used by other Plan/Execute
            // tools — keeps the verifier's read path uniform.
            state.ExecutedPlans[planId] = JsonSerializer.Serialize(new
            {
                action = "assign",
                appId = parsedAppId,
                roleId = parsedRoleId,
                timestamp = DateTime.UtcNow,
            });

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                ToolName = "AssignAppRole",
                Parameters = new { appId = parsedAppId, roleId = parsedRoleId },
                Result = new { appId = parsedAppId, roleId = parsedRoleId },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "UnassignAppRole",
                    Parameters = new { appId = parsedAppId, roleId = parsedRoleId },
                    Warning = "Removes the role from the app. The role itself is untouched.",
                    RiskLevel = RiskLevel.Low,
                },
            });
            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.UIBuilder,
                Action = "AssignAppRole",
                Detail = $"Assigned role {parsedRoleId} to app {parsedAppId} (planId={planId})",
                Success = true,
            });
            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("tool.name", "AssignAppRole"),
                new KeyValuePair<string, object?>("tool.status", "success"));
            return new AppRoleAssignmentResult(planId, true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "AssignAppRole failed: appId={AppId}, roleId={RoleId}", parsedAppId, parsedRoleId);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("tool.name", "AssignAppRole"),
                new KeyValuePair<string, object?>("tool.status", "error"));
            return new AppRoleAssignmentResult(string.Empty, false, ex.Message);
        }
    }
}

/// <summary>
/// Outcome of an <c>assign_app_role</c> or <c>unassign_app_role</c> call.
/// PlanId is non-empty on success; the Tester uses it to find the recorded
/// assignment in <c>state.ExecutedPlans</c>.
/// </summary>
public sealed record AppRoleAssignmentResult(string PlanId, bool Succeeded, string? Error);
