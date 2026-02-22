using System.ComponentModel;
using System.Text;
using System.Text.Json;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateSecurityRoleTool
{
    private static readonly string[] ValidDepths = ["none", "user", "businessUnit", "parentChildBU", "organization"];

    [Description("Generate a plan for creating a custom security role in Dataverse. Returns the privilege matrix, "
        + "OData JSON payload, warnings, and risk level. Does NOT create the role — use execute_create_security_role for that.")]
    public Task<SecurityRoleCreationPlan> PlanCreateSecurityRoleAsync(
        [Description("The name for the security role (e.g., 'Work Order Contributor')")] string roleName,
        [Description("Description of the role's purpose")] string description,
        [Description("Optional: GUID of an existing role to clone as a starting point (empty for new)")] string? copyFromRoleId,
        [Description("Privilege definitions in format: 'table:create:read:write:delete:append:appendTo:assign:share' separated by semicolons. "
            + "Depth values: none, user, businessUnit, parentChildBU, organization. "
            + "Example: 'cr123_workorder:businessUnit:organization:businessUnit:user:businessUnit:businessUnit:businessUnit:businessUnit'")] string privilegesDefinition,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        var copyFromId = !string.IsNullOrWhiteSpace(copyFromRoleId) && Guid.TryParse(copyFromRoleId, out var parsed)
            ? (Guid?)parsed
            : null;

        var privileges = ParsePrivileges(privilegesDefinition, warnings);

        if (privileges.Count == 0)
            warnings.Add("No privileges defined. The role will have no access to any tables.");

        // Check for over-privileged patterns
        foreach (var priv in privileges)
        {
            if (priv.Delete?.Equals("organization", StringComparison.OrdinalIgnoreCase) == true)
                warnings.Add($"Organization-level Delete on '{priv.TableName}' is very permissive. Consider using businessUnit or user depth.");

            if (priv.Assign?.Equals("organization", StringComparison.OrdinalIgnoreCase) == true)
                warnings.Add($"Organization-level Assign on '{priv.TableName}' allows reassigning any record. Consider a narrower depth.");
        }

        // Build privilege matrix
        var matrix = BuildPrivilegeMatrix(privileges);

        // Build OData payload
        var payload = new Dictionary<string, object>
        {
            ["name"] = roleName,
            ["description"] = description
        };

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        return Task.FromResult(new SecurityRoleCreationPlan(
            RoleName: roleName,
            Description: description,
            CopyFromRoleId: copyFromId,
            Privileges: privileges.AsReadOnly(),
            PrivilegeMatrix: matrix,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "orange"));
    }

    private static List<RolePrivilegeSpec> ParsePrivileges(string definition, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return [];

        var privileges = new List<RolePrivilegeSpec>();
        foreach (var entry in definition.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(':', 9);
            if (parts.Length < 2)
            {
                warnings.Add($"Skipping invalid privilege definition: '{entry}'");
                continue;
            }

            var tableName = parts[0].Trim();
            privileges.Add(new RolePrivilegeSpec(
                TableName: tableName,
                Create: GetDepthOrNull(parts, 1),
                Read: GetDepthOrNull(parts, 2),
                Write: GetDepthOrNull(parts, 3),
                Delete: GetDepthOrNull(parts, 4),
                Append: GetDepthOrNull(parts, 5),
                AppendTo: GetDepthOrNull(parts, 6),
                Assign: GetDepthOrNull(parts, 7),
                Share: GetDepthOrNull(parts, 8)));

            // Validate depth values
            for (var i = 1; i < parts.Length; i++)
            {
                var depth = parts[i].Trim();
                if (!string.IsNullOrEmpty(depth) && !ValidDepths.Contains(depth, StringComparer.OrdinalIgnoreCase))
                    warnings.Add($"Invalid depth '{depth}' for table '{tableName}'. Expected: {string.Join(", ", ValidDepths)}.");
            }
        }

        return privileges;
    }

    private static string? GetDepthOrNull(string[] parts, int index)
    {
        if (index >= parts.Length) return null;
        var value = parts[index].Trim();
        return string.IsNullOrEmpty(value) || value.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : value;
    }

    private static string BuildPrivilegeMatrix(List<RolePrivilegeSpec> privileges)
    {
        if (privileges.Count == 0)
            return "(empty — no privileges defined)";

        var sb = new StringBuilder();
        sb.AppendLine("| Table | Create | Read | Write | Delete | Append | AppendTo | Assign | Share |");
        sb.AppendLine("|-------|--------|------|-------|--------|--------|----------|--------|-------|");

        foreach (var p in privileges)
        {
            sb.AppendLine($"| {p.TableName} | {p.Create ?? "—"} | {p.Read ?? "—"} | {p.Write ?? "—"} | {p.Delete ?? "—"} | {p.Append ?? "—"} | {p.AppendTo ?? "—"} | {p.Assign ?? "—"} | {p.Share ?? "—"} |");
        }

        return sb.ToString().TrimEnd();
    }
}
