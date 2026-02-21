namespace DataverseForge.Domain.Models;

public record FieldModificationPlan(
    string TableLogicalName,
    string FieldLogicalName,
    IReadOnlyList<FieldModification> Modifications,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

public record FieldModification(
    string Property,
    string OldValue,
    string NewValue);
