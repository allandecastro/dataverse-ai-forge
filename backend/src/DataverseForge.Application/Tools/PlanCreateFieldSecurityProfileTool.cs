using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public partial class PlanCreateFieldSecurityProfileTool
{
    private static readonly string[] PiiPatterns =
    [
        "email", "phone", "mobile", "fax", "address", "street", "city", "zip", "postal",
        "birthdate", "dateofbirth", "dob", "ssn", "nationalid", "passport", "driverslicense",
        "salary", "compensation", "bankaccount", "creditcard", "revenue", "income",
        "health", "medical", "diagnosis", "prescription", "insurance",
        "settlement", "casefile", "confidential", "secret", "password"
    ];

    [Description("Generate a plan for creating a field-level security profile in Dataverse. "
        + "Returns the profile spec, detected sensitive fields, OData JSON payload, warnings, and risk level. "
        + "Does NOT create the profile — use execute_create_field_security_profile for that.")]
    public Task<FieldSecurityProfileCreationPlan> PlanCreateFieldSecurityProfileAsync(
        [Description("The profile name (e.g., 'PII Access - HR Team')")] string profileName,
        [Description("Description of the profile's purpose")] string description,
        [Description("Field permissions in format: 'table:field:canRead:canUpdate:canCreate' separated by semicolons. "
            + "Example: 'cr123_employee:cr123_salary:true:true:true;cr123_employee:cr123_ssn:true:false:false'")] string fieldPermissionsDefinition,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var sensitiveDetected = new List<string>();

        var permissions = ParseFieldPermissions(fieldPermissionsDefinition, warnings);

        if (permissions.Count == 0)
            warnings.Add("No field permissions defined. The profile will have no effect.");

        // Detect PII/sensitive fields
        foreach (var perm in permissions)
        {
            var fieldLower = perm.FieldLogicalName.ToLowerInvariant();
            foreach (var pattern in PiiPatterns)
            {
                if (fieldLower.Contains(pattern))
                {
                    var category = CategorizePii(pattern);
                    sensitiveDetected.Add($"{perm.TableLogicalName}.{perm.FieldLogicalName} ({category})");
                    break;
                }
            }
        }

        // Warn about fields that grant full access to PII
        foreach (var perm in permissions)
        {
            if (perm.CanRead && perm.CanUpdate && perm.CanCreate)
            {
                var fieldLower = perm.FieldLogicalName.ToLowerInvariant();
                if (PiiPatterns.Any(p => fieldLower.Contains(p)))
                    warnings.Add($"Full access (read+update+create) granted to sensitive field '{perm.FieldLogicalName}'. Consider restricting update/create permissions.");
            }
        }

        warnings.Add("Field-level security requires IsSecured=true on each field. Once enabled, it CANNOT be disabled.");

        // Build payload
        var payload = new Dictionary<string, object>
        {
            ["name"] = profileName,
            ["description"] = description
        };

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        return Task.FromResult(new FieldSecurityProfileCreationPlan(
            ProfileName: profileName,
            Description: description,
            FieldPermissions: permissions.AsReadOnly(),
            SensitiveFieldsDetected: sensitiveDetected.AsReadOnly(),
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "orange"));
    }

    private static List<FieldPermission> ParseFieldPermissions(string definition, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return [];

        var permissions = new List<FieldPermission>();
        foreach (var entry in definition.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(':', 5);
            if (parts.Length < 3)
            {
                warnings.Add($"Skipping invalid permission definition: '{entry}'");
                continue;
            }

            permissions.Add(new FieldPermission(
                parts[0].Trim(),
                parts[1].Trim(),
                parts.Length > 2 && bool.TryParse(parts[2].Trim(), out var canRead) && canRead,
                parts.Length > 3 && bool.TryParse(parts[3].Trim(), out var canUpdate) && canUpdate,
                parts.Length > 4 && bool.TryParse(parts[4].Trim(), out var canCreate) && canCreate));
        }

        return permissions;
    }

    private static string CategorizePii(string pattern) => pattern switch
    {
        "email" or "phone" or "mobile" or "fax" or "address" or "street" or "city" or "zip" or "postal" => "Personal",
        "birthdate" or "dateofbirth" or "dob" or "ssn" or "nationalid" or "passport" or "driverslicense" => "Personal/Identity",
        "salary" or "compensation" or "bankaccount" or "creditcard" or "revenue" or "income" => "Financial",
        "health" or "medical" or "diagnosis" or "prescription" or "insurance" => "Medical",
        "settlement" or "casefile" or "confidential" or "secret" or "password" => "Legal/Confidential",
        _ => "Sensitive"
    };
}
