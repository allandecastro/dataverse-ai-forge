using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ValidateSchemaDesignTool
{
    private static readonly HashSet<string> ReservedWords =
    [
        "account", "contact", "lead", "opportunity", "case", "activity",
        "email", "task", "note", "appointment", "team", "user",
        "businessunit", "organization", "role", "systemuser",
        "solution", "publisher", "connection", "queue"
    ];

    private static readonly Regex ValidSchemaNameRegex = new(@"^[a-zA-Z][a-zA-Z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex PrefixRegex = new(@"^[a-z]+_", RegexOptions.Compiled);

    [Description("Validate a proposed schema design against Dataverse best practices and limits. "
        + "Returns a score 0-100, issues found, and recommendations. "
        + "Use this to review table/field/relationship designs before execution.")]
    public Task<SchemaDesignReview> ValidateSchemaDesignAsync(
        [Description("JSON string containing the proposed schema design with tables, fields, and relationships")]
        string proposedSpecJson,
        [Description("Comma-separated validation rule categories to check: naming, relationships, performance, security, limits, bestPractice, or 'all' for everything")]
        string validationRules,
        CancellationToken cancellationToken)
    {
        var categories = ParseCategories(validationRules);
        var issues = new List<SchemaDesignIssue>();
        var recommendations = new List<string>();

        JsonElement spec;
        try
        {
            spec = JsonSerializer.Deserialize<JsonElement>(proposedSpecJson);
        }
        catch (JsonException)
        {
            issues.Add(new SchemaDesignIssue("error", "parse", "Invalid JSON in proposed spec", "Provide valid JSON"));
            var failReview = BuildReview(issues, recommendations);
            return Task.FromResult(failReview);
        }

        if (spec.TryGetProperty("tables", out var tables))
        {
            foreach (var table in tables.EnumerateArray())
            {
                var tableName = table.TryGetProperty("name", out var tn) ? tn.GetString() ?? "" : "";

                if (categories.Contains("naming"))
                    ValidateNaming(tableName, "table", issues, recommendations);

                if (table.TryGetProperty("fields", out var fields))
                {
                    if (categories.Contains("limits"))
                        ValidateFieldLimit(tableName, fields, issues);

                    foreach (var field in fields.EnumerateArray())
                    {
                        var fieldName = field.TryGetProperty("name", out var fn) ? fn.GetString() ?? "" : "";

                        if (categories.Contains("naming"))
                            ValidateNaming(fieldName, "field", issues, recommendations);

                        if (categories.Contains("security"))
                            ValidateFieldSecurity(tableName, field, fieldName, issues, recommendations);

                        if (categories.Contains("performance"))
                            ValidateFieldPerformance(tableName, field, fieldName, issues);
                    }
                }

                if (categories.Contains("bestPractice"))
                    ValidateBestPractices(tableName, table, issues, recommendations);

                if (categories.Contains("security"))
                    ValidateTableSecurity(tableName, table, issues, recommendations);
            }

            if (categories.Contains("limits"))
                ValidateTableCountLimit(tables, issues);
        }

        if (spec.TryGetProperty("relationships", out var relationships) && categories.Contains("relationships"))
        {
            ValidateRelationships(relationships, issues, recommendations);
        }

        var review = BuildReview(issues, recommendations);
        return Task.FromResult(review);
    }

    private static HashSet<string> ParseCategories(string validationRules)
    {
        var raw = validationRules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var categories = new HashSet<string>(raw, StringComparer.OrdinalIgnoreCase);

        if (categories.Contains("all"))
        {
            return ["naming", "relationships", "performance", "security", "limits", "bestPractice"];
        }

        return categories;
    }

    private static void ValidateNaming(string name, string entityType, List<SchemaDesignIssue> issues, List<string> recommendations)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            issues.Add(new SchemaDesignIssue("error", "naming", $"Empty {entityType} name", $"Provide a valid {entityType} name"));
            return;
        }

        if (!PrefixRegex.IsMatch(name))
        {
            issues.Add(new SchemaDesignIssue("warning", "naming",
                $"{entityType} '{name}' is missing a publisher prefix",
                $"Add a publisher prefix (e.g., 'cr_{{name}}')"));
        }

        if (name.Contains(' ') || name.Contains('-'))
        {
            issues.Add(new SchemaDesignIssue("error", "naming",
                $"{entityType} '{name}' contains spaces or hyphens",
                "Use underscores instead of spaces or hyphens"));
        }

        if (!ValidSchemaNameRegex.IsMatch(name))
        {
            issues.Add(new SchemaDesignIssue("error", "naming",
                $"{entityType} '{name}' contains invalid characters",
                "Use only letters, numbers, and underscores; must start with a letter"));
        }

        if (name.Length > 128)
        {
            issues.Add(new SchemaDesignIssue("error", "naming",
                $"{entityType} '{name}' exceeds 128-character limit",
                "Shorten the name to 128 characters or fewer"));
        }

        var baseName = PrefixRegex.Replace(name, "").ToLowerInvariant();
        if (ReservedWords.Contains(baseName))
        {
            issues.Add(new SchemaDesignIssue("warning", "naming",
                $"{entityType} '{name}' uses a reserved/OOB name '{baseName}'",
                "Choose a more specific name to avoid confusion with OOB tables"));
        }
    }

    private static void ValidateFieldLimit(string tableName, JsonElement fields, List<SchemaDesignIssue> issues)
    {
        var count = 0;
        foreach (var _ in fields.EnumerateArray())
            count++;

        if (count >= 1024)
        {
            issues.Add(new SchemaDesignIssue("error", "limits",
                $"Table '{tableName}' has {count} fields, exceeding the 1024 limit",
                "Reduce the number of fields or split into multiple related tables"));
        }
    }

    private static void ValidateTableCountLimit(JsonElement tables, List<SchemaDesignIssue> issues)
    {
        var count = 0;
        foreach (var _ in tables.EnumerateArray())
            count++;

        if (count > 500)
        {
            issues.Add(new SchemaDesignIssue("warning", "limits",
                $"Design contains {count} tables which is unusually high",
                "Consider consolidating related tables or using polymorphic relationships"));
        }
    }

    private static void ValidateFieldSecurity(string tableName, JsonElement field, string fieldName,
        List<SchemaDesignIssue> issues, List<string> recommendations)
    {
        var isPii = false;
        var lowerName = fieldName.ToLowerInvariant();

        if (lowerName.Contains("ssn") || lowerName.Contains("socialsecurity") ||
            lowerName.Contains("passport") || lowerName.Contains("creditcard") ||
            lowerName.Contains("bankaccount") || lowerName.Contains("salary") ||
            lowerName.Contains("dob") || lowerName.Contains("dateofbirth") ||
            lowerName.Contains("taxid") || lowerName.Contains("nationalid"))
        {
            isPii = true;
        }

        if (field.TryGetProperty("isPii", out var piiProp) && piiProp.GetBoolean())
        {
            isPii = true;
        }

        if (isPii)
        {
            var hasFieldSecurity = field.TryGetProperty("fieldLevelSecurity", out var fls) && fls.GetBoolean();
            if (!hasFieldSecurity)
            {
                issues.Add(new SchemaDesignIssue("error", "security",
                    $"PII field '{fieldName}' on '{tableName}' lacks field-level security",
                    "Enable field-level security for fields containing personally identifiable information"));
                recommendations.Add($"Create a field security profile for '{fieldName}' on '{tableName}'");
            }
        }
    }

    private static void ValidateTableSecurity(string tableName, JsonElement table,
        List<SchemaDesignIssue> issues, List<string> recommendations)
    {
        var hasSensitiveData = false;
        var lowerName = tableName.ToLowerInvariant();
        if (lowerName.Contains("employee") || lowerName.Contains("payroll") ||
            lowerName.Contains("medical") || lowerName.Contains("health") ||
            lowerName.Contains("financial") || lowerName.Contains("salary"))
        {
            hasSensitiveData = true;
        }

        if (hasSensitiveData)
        {
            var hasAudit = table.TryGetProperty("isAuditEnabled", out var audit) && audit.GetBoolean();
            if (!hasAudit)
            {
                issues.Add(new SchemaDesignIssue("warning", "security",
                    $"Sensitive table '{tableName}' does not have auditing enabled",
                    "Enable auditing on tables containing sensitive data"));
            }
        }
    }

    private static void ValidateFieldPerformance(string tableName, JsonElement field, string fieldName,
        List<SchemaDesignIssue> issues)
    {
        var fieldType = field.TryGetProperty("type", out var ft) ? ft.GetString() ?? "" : "";
        if (fieldType.Equals("rollup", StringComparison.OrdinalIgnoreCase))
        {
            var isHighVolume = field.TryGetProperty("highVolume", out var hv) && hv.GetBoolean();
            if (isHighVolume)
            {
                issues.Add(new SchemaDesignIssue("warning", "performance",
                    $"Rollup field '{fieldName}' on '{tableName}' is marked as high-volume",
                    "Consider using a batch calculation job instead of a real-time rollup for high-volume tables"));
            }
        }
    }

    private static void ValidateBestPractices(string tableName, JsonElement table,
        List<SchemaDesignIssue> issues, List<string> recommendations)
    {
        var hasDescription = table.TryGetProperty("description", out var desc) &&
                             !string.IsNullOrWhiteSpace(desc.GetString());
        if (!hasDescription)
        {
            issues.Add(new SchemaDesignIssue("info", "bestPractice",
                $"Table '{tableName}' is missing a description",
                "Add a description to improve discoverability and documentation"));
        }

        var hasAudit = table.TryGetProperty("isAuditEnabled", out var audit) && audit.GetBoolean();
        if (!hasAudit)
        {
            issues.Add(new SchemaDesignIssue("info", "bestPractice",
                $"Table '{tableName}' does not have auditing enabled",
                "Consider enabling auditing for change tracking"));
        }

        var hasSearch = table.TryGetProperty("isSearchEnabled", out var search) && search.GetBoolean();
        if (!hasSearch)
        {
            issues.Add(new SchemaDesignIssue("info", "bestPractice",
                $"Table '{tableName}' does not have Dataverse search enabled",
                "Consider enabling Dataverse search for better findability"));
        }

        var hasAlternateKey = table.TryGetProperty("alternateKeys", out var keys) &&
                              keys.ValueKind == JsonValueKind.Array &&
                              keys.GetArrayLength() > 0;
        if (!hasAlternateKey)
        {
            recommendations.Add($"Consider adding alternate keys to '{tableName}' for integration scenarios");
        }
    }

    private static void ValidateRelationships(JsonElement relationships,
        List<SchemaDesignIssue> issues, List<string> recommendations)
    {
        var graph = new Dictionary<string, List<string>>();

        foreach (var rel in relationships.EnumerateArray())
        {
            var parent = rel.TryGetProperty("parentTable", out var pt) ? pt.GetString() ?? "" : "";
            var child = rel.TryGetProperty("childTable", out var ct) ? ct.GetString() ?? "" : "";
            var cascade = rel.TryGetProperty("cascadeDelete", out var cd) ? cd.GetString() ?? "" : "";

            if (cascade.Equals("cascade", StringComparison.OrdinalIgnoreCase) ||
                cascade.Equals("RemoveLink", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new SchemaDesignIssue("warning", "relationships",
                    $"Cascade delete from '{parent}' to '{child}' may cause data loss",
                    "Consider using 'Restrict' or 'RemoveLink' instead of cascade delete"));
            }

            if (!graph.ContainsKey(parent))
                graph[parent] = [];
            graph[parent].Add(child);
        }

        // Simple circular reference detection
        foreach (var node in graph.Keys)
        {
            if (HasCycle(graph, node, [], []))
            {
                issues.Add(new SchemaDesignIssue("error", "relationships",
                    $"Circular relationship detected involving '{node}'",
                    "Remove or restructure relationships to eliminate the cycle"));
                break; // Report once
            }
        }

        var joinCount = 0;
        foreach (var _ in relationships.EnumerateArray())
            joinCount++;

        if (joinCount > 10)
        {
            recommendations.Add("Consider simplifying the relationship model — views with more than 10 joins may have performance issues");
        }
    }

    private static bool HasCycle(Dictionary<string, List<string>> graph, string node,
        HashSet<string> visited, HashSet<string> inStack)
    {
        if (inStack.Contains(node))
            return true;
        if (visited.Contains(node))
            return false;

        visited.Add(node);
        inStack.Add(node);

        if (graph.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (HasCycle(graph, neighbor, visited, inStack))
                    return true;
            }
        }

        inStack.Remove(node);
        return false;
    }

    private static SchemaDesignReview BuildReview(List<SchemaDesignIssue> issues, List<string> recommendations)
    {
        var score = 100;
        foreach (var issue in issues)
        {
            score -= issue.Severity switch
            {
                "error" => 10,
                "warning" => 5,
                "info" => 1,
                _ => 0
            };
        }
        score = Math.Clamp(score, 0, 100);

        var hasErrors = issues.Any(i => i.Severity == "error");
        var hasWarnings = issues.Any(i => i.Severity == "warning");

        var verdict = (score, hasErrors) switch
        {
            ( <= 30, _) => "REJECTED",
            (_, true) => "NEEDS_CHANGES",
            _ when hasWarnings => "APPROVED_WITH_WARNINGS",
            _ => "APPROVED"
        };

        var riskLevel = score switch
        {
            >= 90 => "LOW",
            >= 70 => "MEDIUM",
            >= 50 => "HIGH",
            _ => "CRITICAL"
        };

        var isValid = !hasErrors;

        return new SchemaDesignReview(isValid, score, verdict, riskLevel, issues, recommendations);
    }
}
