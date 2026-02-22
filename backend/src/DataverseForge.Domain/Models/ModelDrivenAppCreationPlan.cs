namespace DataverseForge.Domain.Models;

public record ModelDrivenAppCreationPlan(
    string AppName,
    string AppUniqueName,
    string Description,
    string SiteMapXml,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);
