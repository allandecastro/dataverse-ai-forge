namespace DataverseForge.Domain.Models;

public record AddOptionPlan(
    string OptionSetName,
    bool IsGlobal,
    string TableLogicalName,
    string FieldLogicalName,
    int NewOptionValue,
    string NewOptionLabel,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);
