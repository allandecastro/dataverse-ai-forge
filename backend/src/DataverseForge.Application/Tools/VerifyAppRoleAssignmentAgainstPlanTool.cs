using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Tester verifier for role assignments. Reads the planId stored by
/// <c>assign_app_role</c> or <c>unassign_app_role</c>, calls
/// <c>list_app_roles</c> to see the live assignments, and compares.
///
/// Uses the same flat-payload-in-ExecutedPlans pattern as the other
/// verifiers — the assign/unassign tools store
/// <c>{action, appId, roleId, timestamp}</c> JSON keyed by the planId.
/// </summary>
public class VerifyAppRoleAssignmentAgainstPlanTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Verify an app-role assignment against the plan. Pass the planId returned "
        + "by assign_app_role or unassign_app_role. Returns PASS if the live app-role "
        + "membership matches the recorded action (assign → role present, unassign → role "
        + "absent), FAIL otherwise. The verifier reads the plan record from state, so the "
        + "agent doesn't need to pass appId/roleId again.")]
    public async Task<AppRoleAssignmentVerificationResult> VerifyAppRoleAssignmentAsync(
        [Description("planId from assign_app_role or unassign_app_role.")] string planId,
        CancellationToken cancellationToken)
    {
        if (!(planId.StartsWith("assign_app_role_", StringComparison.Ordinal)
              || planId.StartsWith("unassign_app_role_", StringComparison.Ordinal)))
        {
            return new AppRoleAssignmentVerificationResult(
                Guid.Empty, Guid.Empty, planId, "FAIL",
                new List<string> { $"WRONG PLAN TYPE: '{planId}' is not an app-role assignment plan. Use the matching verify_*_against_plan instead." }.AsReadOnly());
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        if (!state.ExecutedPlans.TryGetValue(planId, out var planJson))
        {
            return new AppRoleAssignmentVerificationResult(
                Guid.Empty, Guid.Empty, planId, "FAIL",
                new List<string> { $"Plan '{planId}' not found in ExecutedPlans. The assign/unassign tool may have failed before recording the plan." }.AsReadOnly());
        }

        // Parse the recorded payload. The assign/unassign tools write a
        // tight shape so we don't need a full DTO — JsonElement is enough.
        Guid appId, roleId;
        string action;
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(planJson);
            appId = Guid.Parse(doc.GetProperty("appId").GetString()!);
            roleId = Guid.Parse(doc.GetProperty("roleId").GetString()!);
            action = doc.GetProperty("action").GetString()!;
        }
        catch (Exception ex)
        {
            return new AppRoleAssignmentVerificationResult(
                Guid.Empty, Guid.Empty, planId, "FAIL",
                new List<string> { $"Could not parse plan payload for '{planId}': {ex.Message}" }.AsReadOnly());
        }

        IReadOnlyList<AppModuleRoleInfo> liveRoles;
        try
        {
            liveRoles = await dataverseClient.ListAppRolesAsync(appId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AppRoleAssignmentVerificationResult(
                appId, roleId, planId, "FAIL",
                new List<string> { $"Could not read app roles for {appId}: {ex.Message}" }.AsReadOnly());
        }

        var rolePresent = liveRoles.Any(r => r.RoleId == roleId);
        var discrepancies = new List<string>();
        var verdict = "PASS";

        if (string.Equals(action, "assign", StringComparison.OrdinalIgnoreCase))
        {
            if (!rolePresent)
            {
                verdict = "FAIL";
                discrepancies.Add(
                    $"role {roleId} expected to be assigned to app {appId} but is NOT in the live role list "
                    + $"(found {liveRoles.Count} other role(s): {string.Join(", ", liveRoles.Select(r => r.Name))}).");
            }
        }
        else if (string.Equals(action, "unassign", StringComparison.OrdinalIgnoreCase))
        {
            if (rolePresent)
            {
                verdict = "FAIL";
                discrepancies.Add(
                    $"role {roleId} expected to be REMOVED from app {appId} but is still present in the live role list.");
            }
        }
        else
        {
            verdict = "FAIL";
            discrepancies.Add($"Unknown action '{action}' in plan payload — expected 'assign' or 'unassign'.");
        }

        return new AppRoleAssignmentVerificationResult(
            appId, roleId, planId, verdict, discrepancies.AsReadOnly());
    }
}
