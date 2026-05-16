using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanUpdateSecurityRoleTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    IDataverseClient dataverseClient)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Description("Generate a plan for UPDATING an existing Dataverse security role — EXPLICIT "
        + "add and remove lists + optional rename. Returns a planId. The tool does NOT compute "
        + "deltas against the live role's privileges — you tell it exactly what to add and what "
        + "to remove. Pass empty arrays '[]' for either to leave that side alone. "
        + "If you want to ADD privileges, put them in addPrivilegesJson and pass '[]' for "
        + "removePrivilegesJson — DO NOT pass the full target matrix; that's what was causing "
        + "the role to lose all its existing privileges. "
        + "REFUSES on managed roles (IsManaged=true) — those can only be edited in the maker portal.")]
    public async Task<RoleUpdatePlan> PlanUpdateSecurityRoleAsync(
        [Description("RoleId (GUID) of the role to update — from get_security_roles")] Guid roleId,
        [Description("Optional new role name. Pass empty string to leave unchanged.")] string newName,
        [Description("Optional new description. Pass empty string to leave unchanged.")] string newDescription,
        [Description("Privileges to ADD or change depth on — JSON array of {tableLogicalName, "
            + "operation, depth}. Same shape as plan_create_security_role. Pass '[]' if you only "
            + "want to remove privileges (or just rename).")]
        string addPrivilegesJson,
        [Description("Privileges to REMOVE — JSON array of {tableLogicalName, operation}. Pass "
            + "'[]' if you only want to add privileges or rename. NEVER pass a list of privileges "
            + "you didn't explicitly ask the user to remove; this is destructive.")]
        string removePrivilegesJson,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        if (roleId == Guid.Empty)
        {
            return Reject("rejected_update_role_invalid", roleId, [
                "REJECTED: RoleId is required. Call get_security_roles first and pass the GUID "
                + "of the role you want to update."
            ]);
        }

        var liveRole = await dataverseClient.GetSecurityRoleAsync(roleId, cancellationToken);
        if (liveRole is null)
        {
            return Reject("rejected_update_role_invalid", roleId, [
                $"REJECTED: Role {roleId} not found in this environment. Verify the RoleId via "
                + "get_security_roles."
            ]);
        }
        if (liveRole.Info.IsManaged)
        {
            return Reject("rejected_update_role_managed", roleId, [
                $"REJECTED: Role '{liveRole.Info.Name}' is managed (shipped via a managed "
                + "solution). Managed roles can't be edited via the Web API — direct the user "
                + "to the Power Platform maker portal."
            ]);
        }

        var livePrivileges = await dataverseClient.GetRolePrivilegesAsync(roleId, cancellationToken);
        var liveByPrivilegeId = livePrivileges.ToDictionary(p => p.PrivilegeId);

        List<RolePrivilegeSpec> addSpecs;
        try
        {
            addSpecs = JsonSerializer.Deserialize<List<RolePrivilegeSpec>>(addPrivilegesJson, JsonOpts) ?? [];
        }
        catch (JsonException ex)
        {
            return Reject("rejected_update_role_invalid", roleId, [
                $"REJECTED: addPrivilegesJson is not valid JSON: {ex.Message}. "
                + "Expected '[]' or an array of {tableLogicalName, operation, depth}."
            ]);
        }

        List<RolePrivilegeSpec> removeSpecs;
        try
        {
            removeSpecs = JsonSerializer.Deserialize<List<RolePrivilegeSpec>>(removePrivilegesJson, JsonOpts) ?? [];
        }
        catch (JsonException ex)
        {
            return Reject("rejected_update_role_invalid", roleId, [
                $"REJECTED: removePrivilegesJson is not valid JSON: {ex.Message}. "
                + "Expected '[]' or an array of {tableLogicalName, operation}."
            ]);
        }

        // Resolve ADD specs against the table's privilege metadata.
        var toAdd = new List<RolePrivilegeAssignment>();
        var addByTable = addSpecs
            .GroupBy(s => (s.TableLogicalName ?? string.Empty).ToLowerInvariant())
            .ToList();
        foreach (var group in addByTable)
        {
            if (string.IsNullOrWhiteSpace(group.Key))
            {
                warnings.Add($"Skipped {group.Count()} add spec(s) with empty tableLogicalName.");
                continue;
            }
            var privs = await dataverseClient.GetPrivilegesForTableAsync(group.Key, cancellationToken);
            if (privs.Count == 0)
            {
                return Reject("rejected_update_role_invalid", roleId, [
                    $"REJECTED: Table '{group.Key}' has no privileges (does not exist in this "
                    + "environment). Verify the logical name with get_tables_list."
                ]);
            }
            foreach (var spec in group)
            {
                var operation = SecurityRoleValidation.NormalizeOperation(spec.Operation);
                if (operation is null)
                {
                    return Reject("rejected_update_role_invalid", roleId, [
                        $"REJECTED: Unknown operation '{spec.Operation}' on '{group.Key}'. "
                        + "Allowed: Create, Read, Write, Delete, Append, AppendTo, Assign, Share."
                    ]);
                }
                var matched = privs.FirstOrDefault(p =>
                    string.Equals(p.AccessRight, operation, StringComparison.OrdinalIgnoreCase));
                if (matched is null)
                {
                    return Reject("rejected_update_role_invalid", roleId, [
                        $"REJECTED: Table '{group.Key}' does not expose '{operation}' privilege."
                    ]);
                }
                var depthWire = SecurityRoleValidation.NormalizeDepth(spec.Depth);
                if (depthWire is null)
                {
                    return Reject("rejected_update_role_invalid", roleId, [
                        $"REJECTED: Unknown depth '{spec.Depth}'. Allowed: User (Basic), "
                        + "BU (Local), ParentChild (Deep), Org (Global)."
                    ]);
                }
                if (!SecurityRoleValidation.IsDepthAllowed(matched, depthWire))
                {
                    return Reject("rejected_update_role_invalid", roleId, [
                        $"REJECTED: Privilege '{matched.Name}' does not allow depth '{depthWire}'."
                    ]);
                }
                toAdd.Add(new RolePrivilegeAssignment(matched.PrivilegeId, depthWire, matched.Name, liveRole.Info.BusinessUnitId));
            }
        }

        // Resolve REMOVE specs against the live role's actual privilege list —
        // we only remove what's actually there to avoid 404 chains. Removes
        // are looked up by (table, operation), the depth is irrelevant.
        var toRemove = new List<RolePrivilegeAssignment>();
        foreach (var spec in removeSpecs)
        {
            var tableLogical = (spec.TableLogicalName ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(tableLogical))
            {
                warnings.Add("Skipped remove spec with empty tableLogicalName.");
                continue;
            }
            var operation = SecurityRoleValidation.NormalizeOperation(spec.Operation);
            if (operation is null)
            {
                return Reject("rejected_update_role_invalid", roleId, [
                    $"REJECTED: Unknown remove operation '{spec.Operation}' on '{tableLogical}'."
                ]);
            }
            // The privilege name follows the pattern prv<Operation><SchemaName>;
            // use the table's privilege metadata to resolve the actual privilege.
            var privs = await dataverseClient.GetPrivilegesForTableAsync(tableLogical, cancellationToken);
            var matched = privs.FirstOrDefault(p =>
                string.Equals(p.AccessRight, operation, StringComparison.OrdinalIgnoreCase));
            if (matched is null)
            {
                warnings.Add($"Skipped remove spec: {operation} on '{tableLogical}' — no matching privilege exposed.");
                continue;
            }
            if (!liveByPrivilegeId.TryGetValue(matched.PrivilegeId, out var liveMatch))
            {
                warnings.Add($"Skipped remove spec: {matched.Name} not currently on the role — nothing to remove.");
                continue;
            }
            toRemove.Add(liveMatch);
        }

        // Sanity: don't allow add+remove on the same privilege id (caller bug).
        var addIds = toAdd.Select(p => p.PrivilegeId).ToHashSet();
        toRemove = toRemove.Where(p => !addIds.Contains(p.PrivilegeId)).ToList();

        // Effective target after the update applies: start from live, apply
        // removes, then upsert adds (adds override depth on conflicts). The
        // verifier reads this to check the final state.
        var effectiveTarget = new Dictionary<Guid, RolePrivilegeAssignment>(liveByPrivilegeId);
        foreach (var r in toRemove) effectiveTarget.Remove(r.PrivilegeId);
        foreach (var a in toAdd) effectiveTarget[a.PrivilegeId] = a;
        var resolvedTarget = effectiveTarget.Values.ToList();

        string? renameNew = string.IsNullOrWhiteSpace(newName) ? null : newName;
        string? descNew = string.IsNullOrWhiteSpace(newDescription) ? null : newDescription;

        if (renameNew is null && descNew is null && toAdd.Count == 0 && toRemove.Count == 0)
        {
            warnings.Add("No-op plan: nothing to rename and no privilege changes. Skip approval.");
        }

        var rolePatchPayload = new Dictionary<string, object>();
        if (renameNew is not null) rolePatchPayload["name"] = renameNew;
        if (descNew is not null) rolePatchPayload["description"] = descNew;

        var compositePayload = new Dictionary<string, object>
        {
            ["roleId"] = roleId,
            ["rolePatch"] = rolePatchPayload,
            ["add"] = toAdd.Select(p => new Dictionary<string, object>
            {
                ["PrivilegeId"] = p.PrivilegeId,
                ["Depth"] = p.Depth,
                ["Name"] = p.DebugName ?? string.Empty,
                ["BusinessUnitId"] = p.BusinessUnitId?.ToString() ?? string.Empty,
            }).ToArray(),
            ["remove"] = toRemove.Select(p => new Dictionary<string, object>
            {
                ["PrivilegeId"] = p.PrivilegeId,
                ["Depth"] = p.Depth,
                ["Name"] = p.DebugName ?? string.Empty,
                ["BusinessUnitId"] = p.BusinessUnitId?.ToString() ?? string.Empty,
            }).ToArray(),
            // Full target set + expected name so the verifier doesn't re-derive them.
            ["target"] = resolvedTarget.Select(p => new Dictionary<string, object>
            {
                ["PrivilegeId"] = p.PrivilegeId,
                ["Depth"] = p.Depth,
                ["Name"] = p.DebugName ?? string.Empty,
                ["BusinessUnitId"] = p.BusinessUnitId?.ToString() ?? string.Empty,
            }).ToArray(),
            ["expectedName"] = renameNew ?? liveRole.Info.Name,
        };

        var payloadJson = JsonSerializer.Serialize(compositePayload, new JsonSerializerOptions { WriteIndented = true });
        var planId = $"update_security_role_{Guid.NewGuid():N}";

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        // green = no-op, yellow = adds only, orange = any removes (live users can break).
        var risk = (toAdd.Count == 0 && toRemove.Count == 0)
            ? "green"
            : (toRemove.Count > 0 ? "orange" : "yellow");

        return new RoleUpdatePlan(
            PlanId: planId,
            RoleId: roleId,
            NewName: renameNew,
            NewDescription: descNew,
            PrivilegesToAdd: toAdd.AsReadOnly(),
            PrivilegesToRemove: toRemove.AsReadOnly(),
            Warnings: warnings.AsReadOnly(),
            RiskLevel: risk);
    }

    private static RoleUpdatePlan Reject(string prefix, Guid roleId, IReadOnlyList<string> reasons) =>
        new(
            PlanId: $"{prefix}_{Guid.NewGuid():N}",
            RoleId: roleId,
            NewName: null,
            NewDescription: null,
            PrivilegesToAdd: Array.Empty<RolePrivilegeAssignment>(),
            PrivilegesToRemove: Array.Empty<RolePrivilegeAssignment>(),
            Warnings: reasons,
            RiskLevel: "red");

}
