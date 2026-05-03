namespace DataverseForge.Domain.Models;

/// <summary>
/// Plan to add a status-reason option to a table's `statuscode` system field.
/// Uses the InsertStatusValue Dataverse action which requires a State mapping
/// (0 = Active, 1 = Inactive). InsertOptionValue does not work for statuscode.
/// </summary>
public record AddStatusOptionPlan(
    string PlanId,
    string TableLogicalName,
    int StateValue,
    string NewStatusValue,
    string NewStatusLabel,
    string Color,
    IReadOnlyList<string> Warnings,
    string RiskLevel);
