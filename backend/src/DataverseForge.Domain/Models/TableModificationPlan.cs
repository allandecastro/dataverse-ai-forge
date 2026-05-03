namespace DataverseForge.Domain.Models;

public record TableModificationPlan(
    string PlanId,
    string TableLogicalName,
    IReadOnlyList<FieldModification> Modifications,
    IReadOnlyList<string> Warnings,
    string RiskLevel);
