using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateSecurityRoleTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    IDataverseClient dataverseClient)
{
    // Agent serializes JSON with lowercase keys (matches the [Description]
    // example). System.Text.Json is case-sensitive by default — without this,
    // every spec deserializes with default values and the planner produces
    // an empty privilege matrix.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Description("Generate a plan for creating a Dataverse security role with a privilege matrix. "
        + "Returns a planId, validation warnings, and risk level. The full role POST + "
        + "AddPrivilegesRole bodies are stored server-side under planId so they cannot be mangled. "
        + "Pass the planId verbatim to execute_create_security_role after approval. "
        + "BEFORE calling this: (1) call get_security_roles(searchTerm) to confirm no duplicate, "
        + "(2) call get_business_units to resolve the BU, (3) call get_privileges_for_table for "
        + "each distinct table in the matrix to resolve real PrivilegeIds.")]
    public async Task<RoleCreationPlan> PlanCreateSecurityRoleAsync(
        [Description("Display name + storage name for the role (e.g. 'Feedback Reviewer'). "
            + "Dataverse role.name is case-sensitive — duplicate names are rejected.")] string name,
        [Description("Description of the role's purpose")] string description,
        [Description("BusinessUnitId (GUID) the role is scoped to — get from get_business_units. "
            + "The root BU is usually correct for custom roles.")] Guid businessUnitId,
        [Description("JSON array of {tableLogicalName, operation, depth}. operation ∈ "
            + "{Create, Read, Write, Delete, Append, AppendTo, Assign, Share}. "
            + "depth ∈ {User, BU, ParentChild, Org} (translates to Basic / Local / Deep / Global "
            + "on the wire). Example: [{\"tableLogicalName\":\"adc_feedback\",\"operation\":\"Read\",\"depth\":\"BU\"},"
            + "{\"tableLogicalName\":\"adc_feedback\",\"operation\":\"Write\",\"depth\":\"BU\"}]")]
        string privilegesJson,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        // Empty name is fatal — Dataverse rejects with a generic 400.
        if (string.IsNullOrWhiteSpace(name))
        {
            return Reject("rejected_role_invalid", name, businessUnitId, [
                "REJECTED: Role name is required. Pass a non-empty 'name' argument."
            ]);
        }

        if (businessUnitId == Guid.Empty)
        {
            return Reject("rejected_role_invalid", name, businessUnitId, [
                "REJECTED: BusinessUnitId is required. Call get_business_units to enumerate "
                + "the available BUs and pass the GUID of the chosen one."
            ]);
        }

        // Parse the privilege spec list. An empty matrix is allowed (creates
        // a role with no privileges — useful for pure inheritance from a
        // role template, though we don't expose templates in v1) but we
        // surface a warning so the user can confirm.
        List<RolePrivilegeSpec> specs;
        try
        {
            specs = JsonSerializer.Deserialize<List<RolePrivilegeSpec>>(privilegesJson, JsonOpts) ?? [];
        }
        catch (JsonException ex)
        {
            return Reject("rejected_role_invalid", name, businessUnitId, [
                $"REJECTED: privilegesJson is not valid JSON: {ex.Message}. "
                + "Expected an array of {tableLogicalName, operation, depth} objects."
            ]);
        }

        if (specs.Count == 0)
        {
            warnings.Add("Role has NO privileges — confirm with the user. Most useful roles have "
                + "at least Read on a few tables. An empty role grants nothing.");
        }

        // Duplicate-name pre-check. Dataverse will 400 on the second create
        // anyway, but rejecting up-front avoids polluting PendingPlans.
        var existing = await dataverseClient.GetSecurityRolesAsync(searchTerm: name, businessUnitId: businessUnitId, cancellationToken: cancellationToken);
        var exact = existing.FirstOrDefault(r =>
            string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)
            && r.BusinessUnitId == businessUnitId);
        if (exact is not null)
        {
            return Reject("rejected_role_exists", name, businessUnitId, [
                $"REJECTED: A role named '{exact.Name}' already exists in this business unit "
                + $"(RoleId: {exact.RoleId}). Use plan_update_security_role to change its "
                + "privileges, or pick a different name."
            ]);
        }

        // Group specs by table so we only call get_privileges_for_table once
        // per distinct table — reduces network round-trips when a matrix
        // covers a single table with multiple operations.
        var byTable = specs
            .GroupBy(s => (s.TableLogicalName ?? string.Empty).ToLowerInvariant())
            .ToList();

        var resolved = new List<RolePrivilegeAssignment>();
        foreach (var group in byTable)
        {
            var tableLogical = group.Key;
            if (string.IsNullOrWhiteSpace(tableLogical))
            {
                warnings.Add($"Skipped {group.Count()} spec(s) with empty tableLogicalName.");
                continue;
            }

            var tablePrivileges = await dataverseClient.GetPrivilegesForTableAsync(tableLogical, cancellationToken);
            if (tablePrivileges.Count == 0)
            {
                return Reject("rejected_role_invalid", name, businessUnitId, [
                    $"REJECTED: Table '{tableLogical}' has no privileges (the table does not "
                    + "exist or has no exposed privileges in this environment). Verify the "
                    + "logical name with get_tables_list — Dataverse logical names are lowercase."
                ]);
            }

            foreach (var spec in group)
            {
                var operation = NormalizeOperation(spec.Operation);
                if (operation is null)
                {
                    return Reject("rejected_role_invalid", name, businessUnitId, [
                        $"REJECTED: Unknown operation '{spec.Operation}' on '{tableLogical}'. "
                        + "Allowed: Create, Read, Write, Delete, Append, AppendTo, Assign, Share."
                    ]);
                }

                var matched = tablePrivileges.FirstOrDefault(p =>
                    string.Equals(p.AccessRight, operation, StringComparison.OrdinalIgnoreCase));
                if (matched is null)
                {
                    return Reject("rejected_role_invalid", name, businessUnitId, [
                        $"REJECTED: Table '{tableLogical}' does not expose '{operation}' "
                        + "privilege. Available: "
                        + string.Join(", ", tablePrivileges.Select(p => p.AccessRight).Distinct())
                        + "."
                    ]);
                }

                var depthWire = NormalizeDepth(spec.Depth);
                if (depthWire is null)
                {
                    return Reject("rejected_role_invalid", name, businessUnitId, [
                        $"REJECTED: Unknown depth '{spec.Depth}' on {operation}/{tableLogical}. "
                        + "Allowed: User (Basic), BU (Local), ParentChild (Deep), Org (Global)."
                    ]);
                }

                if (!IsDepthAllowed(matched, depthWire))
                {
                    return Reject("rejected_role_invalid", name, businessUnitId, [
                        $"REJECTED: Privilege '{matched.Name}' does not allow depth '{depthWire}'. "
                        + "Allowed depths: " + DescribeAllowedDepths(matched) + "."
                    ]);
                }

                resolved.Add(new RolePrivilegeAssignment(matched.PrivilegeId, depthWire, matched.Name));
            }
        }

        // Build the composite payload. Stored as a single JSON object so the
        // execute tool can deserialize both halves without juggling multiple
        // pending-plan keys.
        var rolePayload = new Dictionary<string, object>
        {
            ["name"] = name,
            ["description"] = description ?? string.Empty,
            ["businessunitid@odata.bind"] = $"/businessunits({businessUnitId})",
        };

        var addPrivilegesPayload = new
        {
            Privileges = resolved.Select(p => new Dictionary<string, object>
            {
                ["@odata.type"] = "Microsoft.Dynamics.CRM.RolePrivilege",
                ["Depth"] = p.Depth,
                ["PrivilegeId"] = p.PrivilegeId,
            }).ToArray()
        };

        var compositePayload = new Dictionary<string, object>
        {
            ["role"] = rolePayload,
            ["privileges"] = resolved.Select(p => new Dictionary<string, object>
            {
                ["PrivilegeId"] = p.PrivilegeId,
                ["Depth"] = p.Depth,
                ["Name"] = p.DebugName ?? string.Empty,
            }).ToArray(),
        };

        var payloadJson = JsonSerializer.Serialize(compositePayload, new JsonSerializerOptions { WriteIndented = true });
        var planId = $"create_security_role_{Guid.NewGuid():N}";

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new RoleCreationPlan(
            PlanId: planId,
            Name: name,
            Description: description ?? string.Empty,
            BusinessUnitId: businessUnitId,
            BusinessUnitName: null,
            Privileges: resolved.AsReadOnly(),
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "orange");
    }

    private static RoleCreationPlan Reject(
        string prefix,
        string name,
        Guid businessUnitId,
        IReadOnlyList<string> rejectionLines) =>
        new(
            PlanId: $"{prefix}_{Guid.NewGuid():N}",
            Name: name ?? string.Empty,
            Description: string.Empty,
            BusinessUnitId: businessUnitId,
            BusinessUnitName: null,
            Privileges: Array.Empty<RolePrivilegeAssignment>(),
            Warnings: rejectionLines,
            RiskLevel: "red");

    /// <summary>
    /// Map agent-supplied operation token to the canonical AccessRight string.
    /// Returns null when the token doesn't match any known access right.
    /// </summary>
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

    /// <summary>
    /// Map agent-supplied depth token to the wire form Dataverse expects on
    /// <c>AddPrivilegesRole</c>. Accepts both the "User / BU / ParentChild /
    /// Org" form (familiar to maker-portal users) and the wire form directly
    /// ("Basic / Local / Deep / Global"). Returns null on unknowns.
    /// </summary>
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

    private static string DescribeAllowedDepths(PrivilegeMetadata p)
    {
        var allowed = new List<string>();
        if (p.CanBeBasic) allowed.Add("User (Basic)");
        if (p.CanBeLocal) allowed.Add("BU (Local)");
        if (p.CanBeDeep) allowed.Add("ParentChild (Deep)");
        if (p.CanBeGlobal) allowed.Add("Org (Global)");
        return allowed.Count == 0 ? "(none)" : string.Join(", ", allowed);
    }
}
