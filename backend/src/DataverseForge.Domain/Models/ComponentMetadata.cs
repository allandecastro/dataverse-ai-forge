namespace DataverseForge.Domain.Models;

public record TableMetadata(
    string LogicalName,
    string DisplayName,
    string SchemaName,
    string OwnershipType,
    bool HasNotes,
    bool HasActivities,
    bool ChangeTrackingEnabled,
    bool IsAuditEnabled);

public record FieldMetadata(
    string LogicalName,
    string DisplayName,
    string AttributeType,
    string RequiredLevel,
    int? MaxLength,
    bool IsSearchable,
    bool IsAuditEnabled);

public record RelationshipMetadata(
    string SchemaName,
    string ReferencedEntity,
    string ReferencedAttribute,
    string ReferencingEntity,
    string ReferencingAttribute,
    string CascadeAssign,
    string CascadeDelete,
    string CascadeReparent,
    string CascadeMerge,
    string CascadeShare,
    string CascadeUnshare);
