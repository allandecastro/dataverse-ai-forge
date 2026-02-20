namespace DataverseForge.Domain.Models;

public record RelationshipCreationPlan(
    string PrimaryTable,
    string RelatedTable,
    string LookupFieldLogicalName,
    string LookupFieldDisplayName,
    string RelationshipSchemaName,
    string CascadeAssign,
    string CascadeDelete,
    string CascadeReparent,
    string CascadeMerge,
    string CascadeShare,
    string CascadeUnshare,
    string LookupBehavior,
    string WebApiPayloadJson,
    string ErDiagramFragment,
    IReadOnlyList<string> Warnings,
    string RiskLevel);
