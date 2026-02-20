namespace DataverseForge.Domain.Models;

public record FieldCreationPlan(
    string TableLogicalName,
    string FieldLogicalName,
    string FieldSchemaName,
    string DisplayName,
    string Description,
    string FieldType,
    string RequiredLevel,
    bool IsSearchable,
    bool IsAuditEnabled,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);
