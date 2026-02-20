namespace DataverseForge.Domain.Models;

public record TableDetails(
    string LogicalName,
    string DisplayName,
    string SchemaName,
    bool IsCustom,
    bool IsManaged,
    string OwnershipType,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<RelationshipInfo> Relationships,
    IReadOnlyList<AlternateKeyInfo> AlternateKeys);

public record ColumnInfo(
    string LogicalName,
    string DisplayName,
    string AttributeType,
    bool IsCustom,
    string RequiredLevel,
    int? MaxLength,
    bool IsSearchable);

public record RelationshipInfo(
    string SchemaName,
    string RelationshipType,
    string ReferencedEntity,
    string ReferencedAttribute,
    string ReferencingEntity,
    string ReferencingAttribute);

public record AlternateKeyInfo(
    string LogicalName,
    string DisplayName,
    IReadOnlyList<string> KeyAttributes);
