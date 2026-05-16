using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Tester-side verifier for bulk privilege grants. Walks every role in the
/// plan, confirms the requested privilege is attached at the requested
/// depth, and rolls per-role verdicts into a composite OverallVerdict.
/// </summary>
public class VerifyBulkPrivilegeGrantAgainstPlanTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Verify a bulk privilege grant against the plan that executed it. Pass the "
        + "planId from execute_grant_privilege_to_roles — the tool loads the plan from server "
        + "state, walks each target role, and checks the requested privilege is attached at "
        + "the requested depth. Returns OverallVerdict (PASS iff every role passes) + per-role "
        + "results with discrepancies.")]
    public async Task<BulkPrivilegeGrantVerificationResult> VerifyBulkPrivilegeGrantAgainstPlanAsync(
        [Description("planId of the bulk grant that was executed — must start with "
            + "grant_privilege_to_roles_")] string planId,
        CancellationToken cancellationToken)
    {
        if (!planId.StartsWith("grant_privilege_to_roles_", StringComparison.Ordinal))
        {
            return new BulkPrivilegeGrantVerificationResult(
                PlanId: planId,
                OverallVerdict: "FAIL",
                PerRoleResults: [new RoleVerificationResult(
                    RoleId: Guid.Empty,
                    PlanId: planId,
                    Verdict: "FAIL",
                    Discrepancies: [new Discrepancy("planId", planId,
                        "WRONG PLAN TYPE — this verifier only accepts "
                        + "grant_privilege_to_roles_* planIds.")])]);
        }

        // AIFunctionFactory swallows uncaught exceptions as "Function failed."
        // — pin every Dataverse call inside a try/catch and convert to a FAIL
        // result with the real message so the Tester reports something useful.
        try
        {

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        if (!state.ExecutedPlans.TryGetValue(planId, out var payloadJson))
        {
            return new BulkPrivilegeGrantVerificationResult(
                PlanId: planId,
                OverallVerdict: "FAIL",
                PerRoleResults: [new RoleVerificationResult(
                    RoleId: Guid.Empty,
                    PlanId: planId,
                    Verdict: "FAIL",
                    Discrepancies: [new Discrepancy("plan", planId, "NOT FOUND in ExecutedPlans")])]);
        }

        var doc = JsonDocument.Parse(payloadJson);
        var privEl = doc.RootElement.GetProperty("privilege");
        var expectedPrivilegeId = privEl.GetProperty("PrivilegeId").GetGuid();
        var expectedDepth = privEl.GetProperty("Depth").GetString() ?? "Basic";
        var expectedName = privEl.TryGetProperty("Name", out var nm) ? nm.GetString() : null;

        // Plan payload uses `roles: [{RoleId, BusinessUnitId}]` shape (commit
        // 559e314). Earlier plans used a flat `roleIds` array — accept both
        // for backwards compat with any in-flight session state.
        var roleIds = new List<Guid>();
        if (doc.RootElement.TryGetProperty("roles", out var rolesArr) && rolesArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in rolesArr.EnumerateArray())
            {
                if (el.TryGetProperty("RoleId", out var ridEl) && ridEl.TryGetGuid(out var rid))
                {
                    roleIds.Add(rid);
                }
            }
        }
        else if (doc.RootElement.TryGetProperty("roleIds", out var legacyArr) && legacyArr.ValueKind == JsonValueKind.Array)
        {
            roleIds.AddRange(legacyArr.EnumerateArray().Select(e => e.GetGuid()));
        }

        var perRole = new List<RoleVerificationResult>();
        foreach (var roleId in roleIds)
        {
            var actualPrivileges = await dataverseClient.GetRolePrivilegesAsync(roleId, cancellationToken);
            var match = actualPrivileges.FirstOrDefault(p => p.PrivilegeId == expectedPrivilegeId);

            var discrepancies = new List<Discrepancy>();
            if (match is null)
            {
                discrepancies.Add(new Discrepancy(
                    Property: $"Privilege.{expectedName ?? expectedPrivilegeId.ToString()}",
                    Expected: expectedDepth,
                    Actual: "<missing>"));
            }
            else if (!string.Equals(match.Depth, expectedDepth, StringComparison.OrdinalIgnoreCase))
            {
                discrepancies.Add(new Discrepancy(
                    Property: $"Privilege.{expectedName ?? expectedPrivilegeId.ToString()}.Depth",
                    Expected: expectedDepth,
                    Actual: match.Depth));
            }

            perRole.Add(new RoleVerificationResult(
                RoleId: roleId,
                PlanId: planId,
                Verdict: discrepancies.Count == 0 ? "PASS" : "FAIL",
                Discrepancies: discrepancies));
        }

        var overall = perRole.All(r => r.Verdict == "PASS") ? "PASS" : "FAIL";
        return new BulkPrivilegeGrantVerificationResult(
            PlanId: planId,
            OverallVerdict: overall,
            PerRoleResults: perRole.AsReadOnly());

        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new BulkPrivilegeGrantVerificationResult(
                PlanId: planId,
                OverallVerdict: "FAIL",
                PerRoleResults: [new RoleVerificationResult(
                    RoleId: Guid.Empty,
                    PlanId: planId,
                    Verdict: "FAIL",
                    Discrepancies: [new Discrepancy("verifier", "exception", ex.Message)])]);
        }
    }
}
