namespace DataverseForge.Domain.Models;

public record VerificationResult(
    bool Exists,
    bool Matches,
    IReadOnlyList<Discrepancy> Discrepancies,
    string Verdict);

public record Discrepancy(
    string Property,
    string? Expected,
    string? Actual);

public record FieldVerificationResult(
    bool Exists,
    bool TypeMatches,
    bool ConstraintsMatch,
    IReadOnlyList<Discrepancy> Discrepancies,
    string Verdict);

public record FullValidationResult(
    ValidationSummary Tables,
    ValidationSummary Fields,
    ValidationSummary Relationships,
    string OverallVerdict,
    string Summary);

public record ValidationSummary(
    int Total,
    int Passed,
    int Failed,
    IReadOnlyList<ComponentDetail> Details);

public record ComponentDetail(
    string Name,
    string Verdict,
    IReadOnlyList<string> Issues);
