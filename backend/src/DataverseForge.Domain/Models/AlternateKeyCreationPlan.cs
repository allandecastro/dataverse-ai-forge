namespace DataverseForge.Domain.Models;

public record AlternateKeyCreationPlan(
    string TableLogicalName,
    string KeyLogicalName,
    string KeyDisplayName,
    IReadOnlyList<string> KeyAttributes,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);
