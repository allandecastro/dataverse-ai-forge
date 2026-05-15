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
    // Agents serialize with lowercase keys; System.Text.Json default is case-sensitive.
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
        CancellationToken cancellationToken,
        [Description("OPTIONAL: RoleTemplateId (GUID) to bind the new role to a template. When "
            + "set, Dataverse auto-replicates this role into every new business unit as it gets "
            + "created — useful for multi-org tenants. Get the GUID from get_role_templates "
            + "first; NEVER invent a template GUID. Leave as Guid.Empty for normal single-BU "
            + "roles.")]
        Guid roleTemplateId = default)
    {
        var warnings = new List<string>();

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

        // One get_privileges_for_table call per distinct table.
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
                var operation = SecurityRoleValidation.NormalizeOperation(spec.Operation);
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

                var depthWire = SecurityRoleValidation.NormalizeDepth(spec.Depth);
                if (depthWire is null)
                {
                    return Reject("rejected_role_invalid", name, businessUnitId, [
                        $"REJECTED: Unknown depth '{spec.Depth}' on {operation}/{tableLogical}. "
                        + "Allowed: User (Basic), BU (Local), ParentChild (Deep), Org (Global)."
                    ]);
                }

                if (!SecurityRoleValidation.IsDepthAllowed(matched, depthWire))
                {
                    return Reject("rejected_role_invalid", name, businessUnitId, [
                        $"REJECTED: Privilege '{matched.Name}' does not allow depth '{depthWire}'. "
                        + "Allowed depths: " + SecurityRoleValidation.DescribeAllowedDepths(matched) + "."
                    ]);
                }

                resolved.Add(new RolePrivilegeAssignment(matched.PrivilegeId, depthWire, matched.Name, businessUnitId));
            }
        }

        var rolePayload = new Dictionary<string, object>
        {
            ["name"] = name,
            ["description"] = description ?? string.Empty,
            ["businessunitid@odata.bind"] = $"/businessunits({businessUnitId})",
        };

        // Template binding tells Dataverse to auto-replicate this role into every new BU.
        if (roleTemplateId != Guid.Empty)
        {
            rolePayload["roletemplateid@odata.bind"] = $"/roletemplates({roleTemplateId})";
            warnings.Add($"Role bound to template {roleTemplateId}: Dataverse will auto-replicate "
                + "this role into every new business unit going forward.");
        }

        var compositePayload = new Dictionary<string, object>
        {
            ["role"] = rolePayload,
            ["privileges"] = resolved.Select(p => new Dictionary<string, object>
            {
                ["PrivilegeId"] = p.PrivilegeId,
                ["Depth"] = p.Depth,
                ["Name"] = p.DebugName ?? string.Empty,
                ["BusinessUnitId"] = p.BusinessUnitId?.ToString() ?? string.Empty,
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

}
