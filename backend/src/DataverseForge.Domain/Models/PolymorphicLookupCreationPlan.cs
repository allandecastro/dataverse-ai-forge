namespace DataverseForge.Domain.Models;

/// <summary>
/// Plan for a multi-table lookup column created via the
/// <c>CreatePolymorphicLookupAttribute</c> Web API action. The lookup belongs to
/// <see cref="ReferencingTable"/> and can point at any of the rows in
/// <see cref="ReferencedTables"/>. One 1:N relationship is created per target
/// table — relationship schema names follow <c>{referencing}_{referenced}</c>
/// unless the user provides an explicit list.
/// </summary>
public record PolymorphicLookupCreationPlan(
    string PlanId,
    string ReferencingTable,
    IReadOnlyList<string> ReferencedTables,
    string LookupSchemaName,
    string LookupDisplayName,
    string Description,
    IReadOnlyList<string> RelationshipSchemaNames,
    IReadOnlyList<string> Warnings,
    string RiskLevel);
