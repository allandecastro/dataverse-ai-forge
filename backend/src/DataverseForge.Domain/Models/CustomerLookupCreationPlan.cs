namespace DataverseForge.Domain.Models;

/// <summary>
/// Plan for a Customer lookup (polymorphic Account+Contact) created via the
/// <c>CreateCustomerRelationships</c> Web API action. Equivalent to a
/// polymorphic lookup with target tables locked to {account, contact}, but
/// uses Dataverse's first-class Customer machinery so the column shows up
/// as type "Customer" in the maker portal rather than "Lookup".
/// </summary>
public record CustomerLookupCreationPlan(
    string PlanId,
    string ReferencingTable,
    string LookupSchemaName,
    string LookupDisplayName,
    string Description,
    string AccountRelationshipSchemaName,
    string ContactRelationshipSchemaName,
    IReadOnlyList<string> Warnings,
    string RiskLevel);
