namespace DataverseForge.Domain.Models;

public record BusinessRuleCreationPlan(
    string TableLogicalName,
    string RuleName,
    string Description,
    string Scope,
    IReadOnlyList<BusinessRuleCondition> Conditions,
    IReadOnlyList<BusinessRuleAction> Actions,
    IReadOnlyList<BusinessRuleAction> ElseActions,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

public record BusinessRuleCondition(
    string Field,
    string Operator,
    string Value,
    string LogicalOperator);

public record BusinessRuleAction(
    string Type,
    string TargetField,
    string? Value);
