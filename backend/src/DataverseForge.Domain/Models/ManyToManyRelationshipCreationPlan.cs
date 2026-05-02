namespace DataverseForge.Domain.Models;

public record ManyToManyRelationshipCreationPlan(
    string PlanId,
    string FirstTable,
    string SecondTable,
    string RelationshipSchemaName,
    string IntersectTableName,
    string WebApiPayloadJson,
    string ErDiagramFragment,
    IReadOnlyList<string> Warnings,
    string RiskLevel);
