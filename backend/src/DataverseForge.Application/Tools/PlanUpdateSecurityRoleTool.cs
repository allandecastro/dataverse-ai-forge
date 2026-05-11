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

    [Description("Generate a plan for UPDATING an existing Dataverse security role — privilege "
        + "delta + optional rename. Returns a planId. The tool reads the LIVE role's privileges "
        + "first, then computes which privileges to add (in target, not in live) and which to "
        + "remove (in live, not in target). Do NOT replace the full privilege set — Dataverse "
        + "doesn't expose an atomic 'set the matrix' call, so a replace becomes "
        + "delete-all-then-re-add, which flickers permissions for in-flight users. "
        + "REFUSES on managed roles (IsManaged=true) — those can only be edited in the maker portal.")]
    public async Task<RoleUpdatePlan> PlanUpdateSecurityRoleAsync(
        [Description("RoleId (GUID) of the role to update — from get_security_roles")] Guid roleId,
        [Description("Optional new role name. Pass empty string to leave unchanged.")] string newName,
        [Description("Optional new description. Pass empty string to leave unchanged.")] string newDescription,
        [Description("Target privilege spec — JSON array of {tableLogicalName, operation, depth}. "
            + "Same shape as plan_create_security_role. The planner diffs this against the live "
            + "privilege set; pass an EMPTY array '[]' to leave the privilege matrix unchanged "
            + "(useful for pure rename / description updates).")]
        string targetPrivilegesJson,
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

        // 1. Read the live role. REFUSE on managed / not found.
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

        // 2. Read live privileges.
        var livePrivileges = await dataverseClient.GetRolePrivilegesAsync(roleId, cancellationToken);
        var liveByPrivilegeId = livePrivileges.ToDictionary(p => p.PrivilegeId);

        // 3. Resolve target privilege spec → privilege GUIDs.
        List<RolePrivilegeSpec> specs;
        try
        {
            specs = JsonSerializer.Deserialize<List<RolePrivilegeSpec>>(targetPrivilegesJson, JsonOpts) ?? [];
        }
        catch (JsonException ex)
        {
            return Reject("rejected_update_role_invalid", roleId, [
                $"REJECTED: targetPrivilegesJson is not valid JSON: {ex.Message}. "
                + "Expected an array of {tableLogicalName, operation, depth} objects "
                + "(or '[]' for no privilege change)."
            ]);
        }

        var resolvedTarget = new List<RolePrivilegeAssignment>();
        var byTable = specs
            .GroupBy(s => (s.TableLogicalName ?? string.Empty).ToLowerInvariant())
            .ToList();
        foreach (var group in byTable)
        {
            if (string.IsNullOrWhiteSpace(group.Key))
            {
                warnings.Add($"Skipped {group.Count()} spec(s) with empty tableLogicalName.");
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
                var operation = NormalizeOperation(spec.Operation);
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
                var depthWire = NormalizeDepth(spec.Depth);
                if (depthWire is null)
                {
                    return Reject("rejected_update_role_invalid", roleId, [
                        $"REJECTED: Unknown depth '{spec.Depth}'. Allowed: User (Basic), "
                        + "BU (Local), ParentChild (Deep), Org (Global)."
                    ]);
                }
                if (!IsDepthAllowed(matched, depthWire))
                {
                    return Reject("rejected_update_role_invalid", roleId, [
                        $"REJECTED: Privilege '{matched.Name}' does not allow depth '{depthWire}'."
                    ]);
                }
                resolvedTarget.Add(new RolePrivilegeAssignment(matched.PrivilegeId, depthWire, matched.Name));
            }
        }

        // 4. Compute delta.
        //    Add  = target \ live           (privilege not currently attached, OR same id with different depth)
        //    Remove = live \ target          (privilege attached but not in target — different depth → handled by remove + re-add)
        //
        // For a depth-change on the same privilege, AddPrivilegesRole is upsert
        // (Dataverse replaces the depth on a re-add), so we only need to ADD —
        // no remove is required for a same-id depth swap. Keep the remove
        // behavior limited to "this privilege is being dropped entirely".
        var targetById = resolvedTarget.ToDictionary(p => p.PrivilegeId);
        var toAdd = new List<RolePrivilegeAssignment>();
        var toRemove = new List<RolePrivilegeAssignment>();

        foreach (var t in resolvedTarget)
        {
            if (!liveByPrivilegeId.TryGetValue(t.PrivilegeId, out var live)
                || !string.Equals(live.Depth, t.Depth, StringComparison.OrdinalIgnoreCase))
            {
                toAdd.Add(t);
            }
        }
        foreach (var l in livePrivileges)
        {
            if (!targetById.ContainsKey(l.PrivilegeId))
            {
                toRemove.Add(l);
            }
        }

        // 5. Optional rename + description PATCH. Empty strings = leave unchanged.
        string? renameNew = string.IsNullOrWhiteSpace(newName) ? null : newName;
        string? descNew = string.IsNullOrWhiteSpace(newDescription) ? null : newDescription;

        if (renameNew is null && descNew is null && toAdd.Count == 0 && toRemove.Count == 0)
        {
            warnings.Add("No-op plan: nothing to rename and no privilege changes. Skip approval.");
        }

        // 6. Store the composite delta payload.
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
            }).ToArray(),
            ["remove"] = toRemove.Select(p => new Dictionary<string, object>
            {
                ["PrivilegeId"] = p.PrivilegeId,
                ["Depth"] = p.Depth,
                ["Name"] = p.DebugName ?? string.Empty,
            }).ToArray(),
            // Persist the full target set so the verifier has the expected
            // post-update state without re-deriving it from the live read.
            ["target"] = resolvedTarget.Select(p => new Dictionary<string, object>
            {
                ["PrivilegeId"] = p.PrivilegeId,
                ["Depth"] = p.Depth,
                ["Name"] = p.DebugName ?? string.Empty,
            }).ToArray(),
            // Keep a copy of the role's expected post-update name so the
            // verifier can compare without re-reading the planner's input.
            ["expectedName"] = renameNew ?? liveRole.Info.Name,
        };

        var payloadJson = JsonSerializer.Serialize(compositePayload, new JsonSerializerOptions { WriteIndented = true });
        var planId = $"update_security_role_{Guid.NewGuid():N}";

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        // Risk: yellow if only adds, orange if any removes (a remove can break
        // a live user's flow). Empty delta → green (no-op).
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

    private static string? NormalizeOperation(string? op) =>
        op?.Trim().ToLowerInvariant() switch
        {
            "create" => "Create",
            "read" => "Read",
            "write" or "update" => "Write",
            "delete" => "Delete",
            "append" => "Append",
            "appendto" => "AppendTo",
            "assign" => "Assign",
            "share" => "Share",
            _ => null,
        };

    private static string? NormalizeDepth(string? depth) =>
        depth?.Trim().ToLowerInvariant() switch
        {
            "user" or "basic" => "Basic",
            "bu" or "local" or "businessunit" or "business-unit" => "Local",
            "parentchild" or "parent-child" or "deep" or "buandbelow" => "Deep",
            "org" or "global" or "organization" => "Global",
            _ => null,
        };

    private static bool IsDepthAllowed(PrivilegeMetadata p, string depthWire) =>
        depthWire switch
        {
            "Basic" => p.CanBeBasic,
            "Local" => p.CanBeLocal,
            "Deep" => p.CanBeDeep,
            "Global" => p.CanBeGlobal,
            _ => false,
        };
}
