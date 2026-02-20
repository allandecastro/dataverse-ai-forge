namespace DataverseForge.Domain.Models;

public record SchemaValidationResult(
    bool IsValid,
    string ValidatedName,
    IReadOnlyList<SchemaValidationIssue> Issues,
    string? Suggestion);

public record SchemaValidationIssue(
    string Rule,
    string Message,
    string Severity);
