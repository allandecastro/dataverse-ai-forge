using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Tester-side verifier for security-role plans. Routes by planId prefix:
/// <c>create_security_role_*</c> diffs the live role + privilege matrix
/// against the planned spec. Commit 2 will add the
/// <c>update_security_role_*</c> branch (post-delta expected state).
/// </summary>
public class VerifySecurityRoleAgainstPlanTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Verify a security role against the plan that created or updated it. "
        + "Pass the planId from the most recent execute_create_security_role / "
        + "execute_update_security_role result — this tool loads the plan from server state and "
        + "diffs the role's name + privilege matrix against the live record. "
        + "Returns PASS / FAIL with discrepancies.")]
    public async Task<RoleVerificationResult> VerifySecurityRoleAgainstPlanAsync(
        [Description("planId of the security role plan that was executed")] string planId,
        CancellationToken cancellationToken)
    {
        var isCreate = planId.StartsWith("create_security_role_", StringComparison.Ordinal);
        var isUpdate = planId.StartsWith("update_security_role_", StringComparison.Ordinal);

        if (!isCreate && !isUpdate)
        {
            return new RoleVerificationResult(
                RoleId: Guid.Empty,
                PlanId: planId,
                Verdict: "FAIL",
                Discrepancies: [new Discrepancy("planId", planId,
                    "WRONG PLAN TYPE — verify_security_role_against_plan only accepts "
                    + "create_security_role_* or update_security_role_* planIds.")]);
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        if (!state.ExecutedPlans.TryGetValue(planId, out var payloadJson))
        {
            return new RoleVerificationResult(
                RoleId: Guid.Empty,
                PlanId: planId,
                Verdict: "FAIL",
                Discrepancies: [new Discrepancy("plan", planId, "NOT FOUND in ExecutedPlans")]);
        }

        if (isCreate)
        {
            return await VerifyCreatePlanAsync(planId, payloadJson, state, cancellationToken);
        }
        return await VerifyUpdatePlanAsync(planId, payloadJson, cancellationToken);
    }

    private async Task<RoleVerificationResult> VerifyUpdatePlanAsync(
        string planId,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var doc = JsonDocument.Parse(payloadJson);
        var roleId = doc.RootElement.GetProperty("roleId").GetGuid();
        var expectedName = doc.RootElement.TryGetProperty("expectedName", out var en)
            ? en.GetString() ?? string.Empty
            : string.Empty;

        // Compare against the FULL target privilege set (the planner persisted
        // it explicitly so we don't have to re-derive it from add/remove deltas).
        var expectedTarget = new List<RolePrivilegeAssignment>();
        if (doc.RootElement.TryGetProperty("target", out var targetArr)
            && targetArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in targetArr.EnumerateArray())
            {
                var id = el.GetProperty("PrivilegeId").GetGuid();
                var depth = el.GetProperty("Depth").GetString() ?? "Basic";
                var name = el.TryGetProperty("Name", out var n) ? n.GetString() : null;
                expectedTarget.Add(new RolePrivilegeAssignment(id, depth, name));
            }
        }

        var liveRole = await dataverseClient.GetSecurityRoleAsync(roleId, cancellationToken);
        if (liveRole is null)
        {
            return new RoleVerificationResult(
                RoleId: roleId,
                PlanId: planId,
                Verdict: "FAIL",
                Discrepancies: [new Discrepancy("role", roleId.ToString(), "NOT FOUND")]);
        }

        var discrepancies = new List<Discrepancy>();

        if (!string.IsNullOrEmpty(expectedName)
            && !string.Equals(liveRole.Info.Name, expectedName, StringComparison.OrdinalIgnoreCase))
        {
            discrepancies.Add(new Discrepancy("Name", expectedName, liveRole.Info.Name));
        }

        var actualPrivileges = await dataverseClient.GetRolePrivilegesAsync(roleId, cancellationToken);
        var actualById = actualPrivileges.ToDictionary(p => p.PrivilegeId);

        foreach (var expected in expectedTarget)
        {
            if (!actualById.TryGetValue(expected.PrivilegeId, out var actual))
            {
                discrepancies.Add(new Discrepancy(
                    Property: $"Privilege.{expected.DebugName ?? expected.PrivilegeId.ToString()}",
                    Expected: expected.Depth,
                    Actual: "<missing>"));
            }
            else if (!string.Equals(actual.Depth, expected.Depth, StringComparison.OrdinalIgnoreCase))
            {
                discrepancies.Add(new Discrepancy(
                    Property: $"Privilege.{expected.DebugName ?? expected.PrivilegeId.ToString()}.Depth",
                    Expected: expected.Depth,
                    Actual: actual.Depth));
            }
        }

        // Privileges still attached but not in target — half-finished remove loop.
        var expectedIds = expectedTarget.Select(p => p.PrivilegeId).ToHashSet();
        foreach (var actual in actualPrivileges.Where(a => !expectedIds.Contains(a.PrivilegeId)))
        {
            discrepancies.Add(new Discrepancy(
                Property: $"Privilege.{actual.DebugName ?? actual.PrivilegeId.ToString()}",
                Expected: "<not in target>",
                Actual: actual.Depth));
        }

        return new RoleVerificationResult(
            RoleId: roleId,
            PlanId: planId,
            Verdict: discrepancies.Count == 0 ? "PASS" : "FAIL",
            Discrepancies: discrepancies);
    }

    private async Task<RoleVerificationResult> VerifyCreatePlanAsync(
        string planId,
        string payloadJson,
        ProjectState state,
        CancellationToken cancellationToken)
    {
        var doc = JsonDocument.Parse(payloadJson);
        var rolePayload = doc.RootElement.GetProperty("role");
        var expectedName = rolePayload.GetProperty("name").GetString() ?? string.Empty;

        var expectedPrivileges = new List<RolePrivilegeAssignment>();
        if (doc.RootElement.TryGetProperty("privileges", out var privArray) &&
            privArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in privArray.EnumerateArray())
            {
                var id = p.GetProperty("PrivilegeId").GetGuid();
                var depth = p.GetProperty("Depth").GetString() ?? "Basic";
                var name = p.TryGetProperty("Name", out var n) ? n.GetString() : null;
                expectedPrivileges.Add(new RolePrivilegeAssignment(id, depth, name));
            }
        }

        // OperationStack carries the roleId from ExecuteCreate; name-lookup is the fallback.
        var roleId = ResolveRoleIdFromOperationStack(state, planId);
        if (roleId == Guid.Empty)
        {
            var byName = await dataverseClient.GetSecurityRolesAsync(searchTerm: expectedName, cancellationToken: cancellationToken);
            var match = byName.FirstOrDefault(r => string.Equals(r.Name, expectedName, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                return new RoleVerificationResult(
                    RoleId: Guid.Empty,
                    PlanId: planId,
                    Verdict: "FAIL",
                    Discrepancies: [new Discrepancy("role", expectedName, "NOT FOUND in environment")]);
            }
            roleId = match.RoleId;
        }

        var liveRole = await dataverseClient.GetSecurityRoleAsync(roleId, cancellationToken);
        if (liveRole is null)
        {
            return new RoleVerificationResult(
                RoleId: roleId,
                PlanId: planId,
                Verdict: "FAIL",
                Discrepancies: [new Discrepancy("role", roleId.ToString(), "NOT FOUND in environment")]);
        }

        var discrepancies = new List<Discrepancy>();

        if (!string.Equals(liveRole.Info.Name, expectedName, StringComparison.OrdinalIgnoreCase))
        {
            discrepancies.Add(new Discrepancy("Name", expectedName, liveRole.Info.Name));
        }

        var actualPrivileges = await dataverseClient.GetRolePrivilegesAsync(roleId, cancellationToken);
        var actualById = actualPrivileges.ToDictionary(p => p.PrivilegeId);

        foreach (var expected in expectedPrivileges)
        {
            if (!actualById.TryGetValue(expected.PrivilegeId, out var actual))
            {
                discrepancies.Add(new Discrepancy(
                    Property: $"Privilege.{expected.DebugName ?? expected.PrivilegeId.ToString()}",
                    Expected: expected.Depth,
                    Actual: "<missing>"));
            }
            else if (!string.Equals(actual.Depth, expected.Depth, StringComparison.OrdinalIgnoreCase))
            {
                discrepancies.Add(new Discrepancy(
                    Property: $"Privilege.{expected.DebugName ?? expected.PrivilegeId.ToString()}.Depth",
                    Expected: expected.Depth,
                    Actual: actual.Depth));
            }
        }

        // Extras — privileges Dataverse auto-attached (Append on the table's
        // primary entity is sometimes auto-included with Create). Surface them
        // as warnings rather than FAILs because they reflect platform behavior
        // outside our plan rather than a bug.
        var expectedIds = expectedPrivileges.Select(p => p.PrivilegeId).ToHashSet();
        foreach (var actual in actualPrivileges.Where(a => !expectedIds.Contains(a.PrivilegeId)))
        {
            discrepancies.Add(new Discrepancy(
                Property: $"Privilege.{actual.DebugName ?? actual.PrivilegeId.ToString()}",
                Expected: "<not in plan>",
                Actual: actual.Depth));
        }

        var verdict = discrepancies.Count == 0 ? "PASS" : "FAIL";
        return new RoleVerificationResult(
            RoleId: roleId,
            PlanId: planId,
            Verdict: verdict,
            Discrepancies: discrepancies);
    }

    private static Guid ResolveRoleIdFromOperationStack(ProjectState state, string planId)
    {
        // ExecuteCreate/UpdateSecurityRole carries planId + roleId on its Result.
        // Match on planId — role names can duplicate across BUs.
        foreach (var op in state.OperationStack)
        {
            if (op.ToolName != "ExecuteCreateSecurityRole" && op.ToolName != "ExecuteUpdateSecurityRole") continue;
            if (op.Result is null) continue;
            try
            {
                var json = JsonSerializer.Serialize(op.Result);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("planId", out var pidEl)
                    || pidEl.ValueKind != JsonValueKind.String
                    || !string.Equals(pidEl.GetString(), planId, StringComparison.Ordinal))
                {
                    continue;
                }
                if (doc.RootElement.TryGetProperty("roleId", out var rid) &&
                    rid.ValueKind == JsonValueKind.String &&
                    Guid.TryParse(rid.GetString(), out var parsed))
                {
                    return parsed;
                }
            }
            catch (JsonException)
            {
                // skip mis-shaped result; the by-name fallback in caller covers it
            }
        }
        return Guid.Empty;
    }
}
