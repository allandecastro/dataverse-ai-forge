namespace DataverseForge.Domain.Models;

public record SchemaDesignReview(
    bool IsValid,
    int Score,
    string Verdict,
    string RiskLevel,
    IReadOnlyList<SchemaDesignIssue> Issues,
    IReadOnlyList<string> Recommendations);

public record SchemaDesignIssue(
    string Severity,
    string Rule,
    string Message,
    string Suggestion);
