namespace DataverseForge.Domain.Models;

// Workflow-entity domain types. Phase 7 of the platform — Automation Builder
// agent. Dataverse's `workflow` entity is the catch-all for processes; the
// `category` column discriminates the shape:
//   0 = Workflow (classic process, out of scope)
//   1 = Dialog (deprecated, out of scope)
//   2 = Business Rule       ← v1 supports this
//   3 = Action (custom action, out of scope)
//   4 = Business Process Flow  ← v1 supports this
//   5 = Modern flow / Power Automate (out of scope, Phase 8)
//
// References:
//   workflow:  https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/workflow
//   SetState:  https://learn.microsoft.com/power-apps/developer/data-platform/webapi/use-web-api-actions#deprecated-message-actions
//   BPF XAML:  https://learn.microsoft.com/dynamics365/customerengagement/on-premises/customize/business-process-flows-overview
//
// Both BPFs and Business Rules store their definitions in the same `clientdata`
// / `xaml` column. The XAML schema is non-trivial — we ship narrow templates
// rather than generic XAML construction (see UNSUPPORTED.md). Anything outside
// the template set falls back to a maker-portal pointer.

/// <summary>
/// One row from <c>get_business_process_flows</c> or <c>get_business_rules</c>.
/// The Category discriminator tells the consumer which shape this is.
/// </summary>
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

/// <summary>
/// Subset of the Dataverse <c>workflow.category</c> picklist that the
/// Automation Builder v1 surface knows how to deal with. Other category
/// values (0 = workflow, 1 = dialog, 3 = action, 5 = modern flow) round-
/// trip through reads but the agent refuses to plan against them.
/// </summary>
public enum WorkflowCategory
{
    BusinessRule = 2,
    BusinessProcessFlow = 4,
}

/// <summary>
/// One Business Process Flow stage. v1 emits stages in declaration order
/// (linear flow); branching navigation is deferred.
/// </summary>
/// <param name="StageName">Display name shown in the BPF widget.</param>
/// <param name="EntityLogicalName">The table the stage's data steps live on. v1 only
/// supports same-entity flows: every stage in a single BPF must share this value
/// with <see cref="BpfCreationPlan.PrimaryEntityLogicalName"/>.</param>
/// <param name="DataSteps">1-5 logical field names the user must fill on this stage.</param>
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

/// <summary>
/// One Business Rule action — v1 supports four shapes. Each maps to a
/// distinct XAML template; multi-action rules require running the rule
/// builder UI in the maker portal and stay out of scope.
/// </summary>
public enum BusinessRuleActionKind
{
    /// <summary>Hide the target field when the condition matches.</summary>
    HideField,
    /// <summary>Show the target field when the condition matches.</summary>
    ShowField,
    /// <summary>Make the target field business-required.</summary>
    SetRequired,
    /// <summary>Make the target field optional.</summary>
    SetOptional,
    /// <summary>Lock the target field (read-only) when the condition matches.</summary>
    LockField,
    /// <summary>Unlock the target field when the condition matches.</summary>
    UnlockField,
    /// <summary>Set the target field to a constant value when the condition matches.</summary>
    SetFieldValue,
}

/// <summary>
/// Plan record returned by <c>plan_create_business_rule</c>. v1 supports
/// one condition + one action — multi-condition AND/OR and nested ELSE
/// branches are out of scope.
/// </summary>
/// <param name="ConditionFieldLogicalName">The field whose value drives the rule.</param>
/// <param name="ConditionOperator">"equals" / "notEquals" / "isNull" / "isNotNull" — v1 set.</param>
/// <param name="ConditionValue">String form of the value to compare against. Ignored for null operators.</param>
/// <param name="TargetFieldLogicalName">The field the action applies to (e.g. the one being hidden).</param>
/// <param name="ActionKind">Which of the four templates to instantiate.</param>
/// <param name="ActionValue">Constant value for <see cref="BusinessRuleActionKind.SetFieldValue"/>. Ignored otherwise.</param>
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

/// <summary>
/// Result returned by <c>verify_workflow_against_plan</c>. Same shape as
/// the SecurityBuilder verifier — PASS / FAIL / PARTIAL with concrete
/// discrepancies plus the live record's metadata for the report.
/// </summary>
public sealed record WorkflowVerificationResult(
    Guid WorkflowId,
    string PlanId,
    string Verdict,
    IReadOnlyList<Discrepancy> Discrepancies);
