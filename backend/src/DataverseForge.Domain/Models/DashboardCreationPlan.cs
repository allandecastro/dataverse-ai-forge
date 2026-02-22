namespace DataverseForge.Domain.Models;

public record DashboardCreationPlan(
    string DashboardName,
    string Description,
    string FormXml,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);
