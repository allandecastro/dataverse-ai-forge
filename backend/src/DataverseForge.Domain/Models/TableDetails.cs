namespace DataverseForge.Domain.Models;

public record TableDetails(
    string LogicalName,
    string DisplayName,
    string SchemaName,
    bool IsCustom,
    bool IsManaged,
    string OwnershipType,
    string PrimaryIdAttribute,
    string PrimaryNameAttribute,
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
    bool IsSearchable,
    bool IsSecured = false);

public record RelationshipInfo(
    string SchemaName,
    string RelationshipType,
    string ReferencedEntity,
    string ReferencedAttribute,
    string ReferencingEntity,
    string ReferencingAttribute,
    // Required for writing lookup values via @odata.bind. Optional because older
    // metadata fetches and tests construct RelationshipInfo without it; new
    // GetTableDetails query always populates it.
    string? ReferencingEntityNavigationPropertyName = null);

public record AlternateKeyInfo(
    string LogicalName,
    string DisplayName,
    IReadOnlyList<string> KeyAttributes,
    Guid MetadataId = default);
