using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class AnalyzeSecurityGapsTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    private static readonly string[] PiiPatterns =
    [
        "email", "phone", "mobile", "fax", "address", "street", "city", "zip", "postal",
        "birthdate", "dateofbirth", "dob", "ssn", "nationalid", "passport", "driverslicense",
        "salary", "compensation", "bankaccount", "creditcard", "revenue", "income",
        "health", "medical", "diagnosis", "prescription", "insurance",
        "settlement", "casefile", "confidential", "secret", "password"
    ];

    [Description("Analyze the current project's security posture and identify gaps. "
        + "Scans created tables and fields for PII without field-level security, "
        + "tables without custom security roles, and over-privileged role patterns. "
        + "Returns actionable recommendations.")]
    public async Task<SecurityGapAnalysis> AnalyzeSecurityGapsAsync(
        CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        var piiFields = new List<PiiFieldWithoutSecurity>();
        var tablesWithoutRoles = new List<string>();
        var overPrivileged = new List<string>();
        var recommendations = new List<string>();

        // Detect PII fields without field-level security
        foreach (var field in state.Fields)
        {
            var fieldLower = field.LogicalName.ToLowerInvariant();
            foreach (var pattern in PiiPatterns)
            {
                if (fieldLower.Contains(pattern))
                {
                    var category = CategorizePii(pattern);
                    piiFields.Add(new PiiFieldWithoutSecurity(field.TableLogicalName, field.LogicalName, category));
                    break;
                }
            }
        }

        // Check which custom tables have no custom security roles
        var roleTableNames = state.SecurityRoles
            .Select(r => r.Name.ToLowerInvariant())
            .ToHashSet();

        foreach (var table in state.Tables)
        {
            var tableNameLower = table.LogicalName.ToLowerInvariant();
            var hasRole = roleTableNames.Any(r => r.Contains(tableNameLower.Replace("_", " "))
                || r.Contains(table.DisplayName.ToLowerInvariant()));

            if (!hasRole)
                tablesWithoutRoles.Add(table.LogicalName);
        }

        // Build recommendations
        if (piiFields.Count > 0)
        {
            recommendations.Add($"Enable field-level security on {piiFields.Count} PII field(s): "
                + string.Join(", ", piiFields.Select(f => $"{f.TableLogicalName}.{f.FieldLogicalName}")));
        }

        if (tablesWithoutRoles.Count > 0)
        {
            recommendations.Add($"Create custom security roles for {tablesWithoutRoles.Count} table(s) without coverage: "
                + string.Join(", ", tablesWithoutRoles));
        }

        if (state.SecurityRoles.Count == 0 && state.Tables.Count > 0)
        {
            recommendations.Add("No custom security roles have been created yet. Consider creating at least Viewer, Contributor, and Manager roles for each entity.");
        }

        if (state.Tables.Count > 0 && piiFields.Count == 0)
        {
            recommendations.Add("No PII fields detected based on naming patterns. If fields contain sensitive data with non-obvious names, consider manual review.");
        }

        return new SecurityGapAnalysis(
            PiiFieldsWithoutSecurity: piiFields.AsReadOnly(),
            TablesWithoutCustomRoles: tablesWithoutRoles.AsReadOnly(),
            OverPrivilegedRoles: overPrivileged.AsReadOnly(),
            Recommendations: recommendations.AsReadOnly());
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
