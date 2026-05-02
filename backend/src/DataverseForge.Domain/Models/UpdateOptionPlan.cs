namespace DataverseForge.Domain.Models;

/// <summary>
/// Plan to rename or delete an option value on an existing option set
/// (global, local picklist, or local statuscode).
/// </summary>
public record UpdateOptionPlan(
    string PlanId,
    string Action,           // "update" (rename) or "delete"
    string OptionSetName,
    bool IsGlobal,
    bool IsStatusCode,
    string TableLogicalName,
    string FieldLogicalName,
    int OptionValue,
    string? NewLabel,        // null when Action == "delete"
    IReadOnlyList<string> Warnings,
    string RiskLevel);
