namespace DataverseForge.Domain.Models;

// PlanId is REQUIRED on these records — the executor calls
// state.LookupPlan(planId, ...) and won't run without it. Both records use
// planId-by-reference so the agent never re-passes the multi-KB
// WebApiBatchPayloadJson back to the executor (CLAUDE.md recurring bug #1).

public record ReferenceDataPlan(
    string PlanId,
    string TableLogicalName,
    string EntitySetName,
    int RecordCount,
    IReadOnlyList<Dictionary<string, object?>> Records,
    string WebApiBatchPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

public record TestDataPlan(
    string PlanId,
    string TableLogicalName,
    string EntitySetName,
    int RecordCount,
    IReadOnlyList<Dictionary<string, object?>> Records,
    string WebApiBatchPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

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
