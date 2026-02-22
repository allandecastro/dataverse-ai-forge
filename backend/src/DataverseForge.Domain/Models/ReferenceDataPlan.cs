namespace DataverseForge.Domain.Models;

public record ReferenceDataPlan(
    string TableLogicalName,
    string EntitySetName,
    int RecordCount,
    IReadOnlyList<Dictionary<string, object?>> Records,
    string WebApiBatchPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

public record TestDataPlan(
    string TableLogicalName,
    string EntitySetName,
    int RecordCount,
    IReadOnlyList<Dictionary<string, object?>> Records,
    string WebApiBatchPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

public record SuggestedReferenceData(
    string TableLogicalName,
    int RecordCount,
    IReadOnlyList<Dictionary<string, object?>> SuggestedRecords,
    IReadOnlyList<string> Notes);

public record BatchOperationResult(
    int SuccessCount,
    int FailureCount,
    IReadOnlyList<Guid> CreatedRecordIds,
    IReadOnlyList<string> Errors);

public record DeleteTestDataPlan(
    string TableLogicalName,
    string EntitySetName,
    int RecordCount,
    IReadOnlyList<Guid> RecordIdsToDelete,
    IReadOnlyList<string> Warnings,
    string RiskLevel);
