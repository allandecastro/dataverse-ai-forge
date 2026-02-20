namespace DataverseForge.Domain.Models;

public record TableCreationPlan(
    string TableLogicalName,
    string TableSchemaName,
    string DisplayName,
    string PluralDisplayName,
    string Description,
    string OwnershipType,
    string PrimaryFieldLogicalName,
    string PrimaryFieldDisplayName,
    int PrimaryFieldMaxLength,
    bool HasNotes,
    bool HasActivities,
    bool ChangeTrackingEnabled,
    bool IsAuditEnabled,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel,
    string SolutionUniqueName);
