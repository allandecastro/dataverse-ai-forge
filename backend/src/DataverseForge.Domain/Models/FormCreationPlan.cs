namespace DataverseForge.Domain.Models;

public record FormCreationPlan(
    string TableLogicalName,
    string FormName,
    string FormType,
    string Description,
    string FormXml,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);
