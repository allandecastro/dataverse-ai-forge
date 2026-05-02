namespace DataverseForge.Domain.Models;

public record AddOptionPlan(
    string PlanId,
    string OptionSetName,
    bool IsGlobal,
    string TableLogicalName,
    string FieldLogicalName,
    int NewOptionValue,
    string NewOptionLabel,
    IReadOnlyList<string> Warnings,
    string RiskLevel);
