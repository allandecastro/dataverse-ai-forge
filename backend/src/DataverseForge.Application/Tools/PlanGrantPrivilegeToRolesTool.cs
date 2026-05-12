using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanGrantPrivilegeToRolesTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    IDataverseClient dataverseClient)
{
    [Description("Generate a plan for granting a SINGLE privilege (one table + one operation + "
        + "one depth) to MULTIPLE roles at once — e.g. 'give Sales User + Sales Manager + "
        + "Sales VP Read on Account at BU depth'. Returns a planId. The full payload is staged "
        + "server-side; pass the planId verbatim to execute_grant_privilege_to_roles after "
        + "approval. Use plan_update_security_role for per-role changes; this tool is the right "
        + "shape only when the SAME (privilege, depth) lands on a SET of roles. "
        + "REFUSES on managed roles in the list, missing roles, invalid privileges, and depths "
        + "Dataverse rejects on the privilege.")]
    public async Task<BulkPrivilegeGrantPlan> PlanGrantPrivilegeToRolesAsync(
        [Description("JSON array of RoleId GUIDs to grant the privilege to — from "
            + "get_security_roles. Order doesn't matter; duplicates are de-duplicated.")]
        string roleIdsJson,
        [Description("Lowercase logical name of the table (e.g. 'account', 'adc_feedback')")]
        string tableLogicalName,
        [Description("Operation to grant: Create | Read | Write | Delete | Append | AppendTo | "
            + "Assign | Share")] string operation,
        [Description("Depth: User (Basic) | BU (Local) | ParentChild (Deep) | Org (Global). "
            + "Same depth applies to every role in the list — for per-role depth variation use "
            + "plan_update_security_role per role instead.")] string depth,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        List<Guid> roleIds;
        try
        {
            roleIds = JsonSerializer.Deserialize<List<Guid>>(roleIdsJson) ?? [];
        }
        catch (JsonException ex)
        {
            return Reject("rejected_bulk_grant_invalid", [
                $"REJECTED: roleIdsJson is not a valid JSON array of GUIDs: {ex.Message}. "
                + "Expected shape: [\"<guid1>\", \"<guid2>\", ...]."
            ]);
        }

        roleIds = roleIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (roleIds.Count == 0)
        {
            return Reject("rejected_bulk_grant_invalid", [
                "REJECTED: roleIds is empty. Provide at least one role to grant the privilege to."
            ]);
        }

        if (string.IsNullOrWhiteSpace(tableLogicalName))
        {
            return Reject("rejected_bulk_grant_invalid", [
                "REJECTED: tableLogicalName is required."
            ]);
        }

        var normalizedOp = SecurityRoleValidation.NormalizeOperation(operation);
        if (normalizedOp is null)
        {
            return Reject("rejected_bulk_grant_invalid", [
                $"REJECTED: Unknown operation '{operation}'. Allowed: Create, Read, Write, "
                + "Delete, Append, AppendTo, Assign, Share."
            ]);
        }

        var normalizedDepth = SecurityRoleValidation.NormalizeDepth(depth);
        if (normalizedDepth is null)
        {
            return Reject("rejected_bulk_grant_invalid", [
                $"REJECTED: Unknown depth '{depth}'. Allowed: User (Basic), BU (Local), "
                + "ParentChild (Deep), Org (Global)."
            ]);
        }

        var lowerTable = tableLogicalName.ToLowerInvariant();
        var privs = await dataverseClient.GetPrivilegesForTableAsync(lowerTable, cancellationToken);
        if (privs.Count == 0)
        {
            return Reject("rejected_bulk_grant_invalid", [
                $"REJECTED: Table '{lowerTable}' has no privileges (does not exist in this "
                + "environment). Verify the logical name with get_tables_list — Dataverse "
                + "logical names are lowercase."
            ]);
        }

        var matched = privs.FirstOrDefault(p =>
            string.Equals(p.AccessRight, normalizedOp, StringComparison.OrdinalIgnoreCase));
        if (matched is null)
        {
            return Reject("rejected_bulk_grant_invalid", [
                $"REJECTED: Table '{lowerTable}' does not expose '{normalizedOp}' privilege. "
                + $"Available: {string.Join(", ", privs.Select(p => p.AccessRight).Distinct())}."
            ]);
        }

        if (!SecurityRoleValidation.IsDepthAllowed(matched, normalizedDepth))
        {
            return Reject("rejected_bulk_grant_invalid", [
                $"REJECTED: Privilege '{matched.Name}' does not allow depth '{normalizedDepth}'. "
                + $"Allowed: {SecurityRoleValidation.DescribeAllowedDepths(matched)}."
            ]);
        }

        var missing = new List<Guid>();
        var managed = new List<(Guid id, string name)>();
        foreach (var roleId in roleIds)
        {
            var role = await dataverseClient.GetSecurityRoleAsync(roleId, cancellationToken);
            if (role is null)
            {
                missing.Add(roleId);
                continue;
            }
            if (role.Info.IsManaged)
            {
                managed.Add((roleId, role.Info.Name));
            }
        }
        if (missing.Count > 0)
        {
            return Reject("rejected_bulk_grant_invalid", [
                "REJECTED: The following RoleIds do not exist in this environment: "
                + string.Join(", ", missing) + ". Verify them via get_security_roles."
            ]);
        }
        if (managed.Count > 0)
        {
            return Reject("rejected_bulk_grant_managed", [
                "REJECTED: The following roles are managed (shipped via a managed solution) "
                + "and cannot be edited via the Web API: "
                + string.Join("; ", managed.Select(m => $"'{m.name}' ({m.id})"))
                + ". Direct the user to the maker portal."
            ]);
        }

        var compositePayload = new Dictionary<string, object>
        {
            ["privilege"] = new Dictionary<string, object>
            {
                ["PrivilegeId"] = matched.PrivilegeId,
                ["Depth"] = normalizedDepth,
                ["Name"] = matched.Name,
                ["AccessRight"] = matched.AccessRight,
            },
            ["roleIds"] = roleIds.ToArray(),
        };

        var payloadJson = JsonSerializer.Serialize(compositePayload, new JsonSerializerOptions { WriteIndented = true });
        var planId = $"grant_privilege_to_roles_{Guid.NewGuid():N}";

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        // Bulk grants carry blast radius across every role in the set.
        return new BulkPrivilegeGrantPlan(
            PlanId: planId,
            PrivilegeId: matched.PrivilegeId,
            PrivilegeName: matched.Name,
            AccessRight: matched.AccessRight,
            Depth: normalizedDepth,
            TargetRoleIds: roleIds.AsReadOnly(),
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "orange");
    }

    private static BulkPrivilegeGrantPlan Reject(string prefix, IReadOnlyList<string> reasons) =>
        new(
            PlanId: $"{prefix}_{Guid.NewGuid():N}",
            PrivilegeId: Guid.Empty,
            PrivilegeName: string.Empty,
            AccessRight: string.Empty,
            Depth: string.Empty,
            TargetRoleIds: Array.Empty<Guid>(),
            Warnings: reasons,
            RiskLevel: "red");

}
