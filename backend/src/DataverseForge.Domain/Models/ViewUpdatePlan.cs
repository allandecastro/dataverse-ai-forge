namespace DataverseForge.Domain.Models;

public record ViewUpdatePlan(
    string PlanId,
    Guid ViewId,
    string TableLogicalName,
    string ViewName,
    string Description,
    string FetchXml,
    string LayoutXml,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);
