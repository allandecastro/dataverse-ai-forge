namespace DataverseForge.Domain.Models;

public record FormUpdatePlan(
    string PlanId,
    Guid FormId,
    string TableLogicalName,
    string FormName,
    string FormType,
    string Description,
    string FormXml,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);
