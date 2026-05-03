namespace DataverseForge.Domain.Models;

public record TableMetadata(
    Guid MetadataId,
    string LogicalName,
    string DisplayName,
    string SchemaName,
    string OwnershipType,
    bool HasNotes,
    bool HasActivities,
    bool ChangeTrackingEnabled,
    bool IsAuditEnabled,
    bool IsActivity = false,
    bool IsActivityParty = false,
    bool IsLogicalEntity = false,
    bool IsRenameable = true);

public record FieldMetadata(
    Guid MetadataId,
    string LogicalName,
    string DisplayName,
    string AttributeType,
    string RequiredLevel,
    int? MaxLength,
    bool IsSearchable,
    bool IsAuditEnabled,
    bool IsSecured = false);

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
