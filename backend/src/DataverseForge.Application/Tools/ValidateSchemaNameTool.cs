using System.ComponentModel;
using System.Text.RegularExpressions;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public partial class ValidateSchemaNameTool
{
    private const int TableMaxLength = 128;
    private const int FieldMaxLength = 64;

    [Description("Validate a proposed Dataverse schema name against naming conventions. Returns validation result with issues and a suggested corrected name if needed.")]
    public Task<SchemaValidationResult> ValidateSchemaNameAsync(
        [Description("The proposed schema name (without publisher prefix)")] string proposedName,
        [Description("The publisher prefix (e.g., 'cr123')")] string prefix,
        [Description("The type of schema object: 'table' (default) or 'field'")] string type,
        CancellationToken cancellationToken)
    {
        var issues = new List<SchemaValidationIssue>();
        var correctedName = proposedName;

        // Rule: no spaces
        if (correctedName.Contains(' '))
        {
            issues.Add(new SchemaValidationIssue("NoSpaces", "Schema names cannot contain spaces.", "Error"));
            correctedName = correctedName.Replace(" ", "");
        }

        // Rule: no hyphens
        if (correctedName.Contains('-'))
        {
            issues.Add(new SchemaValidationIssue("NoHyphens", "Schema names cannot contain hyphens.", "Error"));
            correctedName = correctedName.Replace("-", "_");
        }

        // Rule: alphanumeric + underscore only
        if (!AlphanumericUnderscoreRegex().IsMatch(correctedName))
        {
            issues.Add(new SchemaValidationIssue("AlphanumericOnly", "Schema names must contain only alphanumeric characters and underscores.", "Error"));
            correctedName = InvalidCharRegex().Replace(correctedName, "");
        }

        // Rule: lowercase logical name
        var lowerName = correctedName.ToLowerInvariant();
        if (correctedName != lowerName)
        {
            issues.Add(new SchemaValidationIssue("Lowercase", "Logical names should be lowercase.", "Warning"));
            correctedName = lowerName;
        }

        // Build full name with prefix
        var fullName = $"{prefix}_{correctedName}";

        // Rule: max length
        var maxLength = type.Equals("field", StringComparison.OrdinalIgnoreCase) ? FieldMaxLength : TableMaxLength;
        if (fullName.Length > maxLength)
        {
            issues.Add(new SchemaValidationIssue("MaxLength", $"Full name '{fullName}' exceeds maximum length of {maxLength} characters (currently {fullName.Length}).", "Error"));
            var availableLength = maxLength - prefix.Length - 1; // -1 for underscore
            if (availableLength > 0)
            {
                correctedName = correctedName[..Math.Min(correctedName.Length, availableLength)];
                fullName = $"{prefix}_{correctedName}";
            }
        }

        // Rule: cannot be empty after corrections
        if (string.IsNullOrEmpty(correctedName))
        {
            issues.Add(new SchemaValidationIssue("NotEmpty", "Schema name cannot be empty after removing invalid characters.", "Error"));
        }

        var isValid = issues.All(i => i.Severity != "Error");
        string? suggestion = issues.Count > 0 ? fullName : null;

        return Task.FromResult(new SchemaValidationResult(
            IsValid: isValid,
            ValidatedName: fullName,
            Issues: issues.AsReadOnly(),
            Suggestion: suggestion));
    }

    [GeneratedRegex(@"^[a-zA-Z0-9_]+$")]
    private static partial Regex AlphanumericUnderscoreRegex();

    [GeneratedRegex(@"[^a-zA-Z0-9_]")]
    private static partial Regex InvalidCharRegex();
}
