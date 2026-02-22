namespace DataverseForge.Domain.Models;

public record FieldSecurityProfileCreationPlan(
    string ProfileName,
    string Description,
    IReadOnlyList<FieldPermission> FieldPermissions,
    IReadOnlyList<string> SensitiveFieldsDetected,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);
