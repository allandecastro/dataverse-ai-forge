namespace DataverseForge.Domain.Models;

// Workflow-entity domain types for AutomationBuilder. Dataverse's `workflow`
// entity uses `category` to discriminate: 0=Workflow, 1=Dialog, 2=BusinessRule,
// 3=Action, 4=BusinessProcessFlow, 5=Power Automate. v1 supports 2 + 4 only.
//   workflow ref: https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/workflow

public sealed record WorkflowInfo(
    Guid WorkflowId,
    string Name,
    string? Description,
    WorkflowCategory Category,
    string PrimaryEntityLogicalName,
    bool IsActivated,
    bool IsManaged,
    bool IsCustomizable,
    int Scope,
    DateTime? ModifiedOn);

public enum WorkflowCategory
{
    BusinessRule = 2,
    BusinessProcessFlow = 4,
}

public sealed record BpfStageSpec(
    string StageName,
    string EntityLogicalName,
    IReadOnlyList<string> DataSteps);

/// <summary>
/// Plan record returned by <c>plan_create_business_process_flow</c>.
/// The composite payload (workflow POST body with embedded XAML) is staged
/// server-side under <see cref="PlanId"/>; the agent passes the planId to
/// <c>execute_create_business_process_flow</c>.
/// </summary>
public sealed record BpfCreationPlan(
    string PlanId,
    string Name,
    string Description,
    string PrimaryEntityLogicalName,
    IReadOnlyList<BpfStageSpec> Stages,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

public enum BusinessRuleActionKind
{
    HideField,
    ShowField,
    SetRequired,
    SetOptional,
    LockField,
    UnlockField,
    SetFieldValue,
}

public sealed record BusinessRuleCreationPlan(
    string PlanId,
    string Name,
    string Description,
    string PrimaryEntityLogicalName,
    string ConditionFieldLogicalName,
    string ConditionOperator,
    string? ConditionValue,
    string TargetFieldLogicalName,
    BusinessRuleActionKind ActionKind,
    string? ActionValue,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

public sealed record WorkflowVerificationResult(
    Guid WorkflowId,
    string PlanId,
    string Verdict,
    IReadOnlyList<Discrepancy> Discrepancies);
