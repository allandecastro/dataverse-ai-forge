namespace DataverseForge.Domain.Models;

public record OobTableRecommendation(
    string OobTableName,
    int MatchConfidence,
    string Explanation,
    IReadOnlyList<string> MissingFields,
    string CoverageSummary);
