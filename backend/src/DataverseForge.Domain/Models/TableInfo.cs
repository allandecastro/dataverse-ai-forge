namespace DataverseForge.Domain.Models;

public record TableInfo(
    string LogicalName,
    string DisplayName,
    string SchemaName,
    bool IsCustom,
    bool IsManaged,
    string OwnershipType,
    string PrimaryIdAttribute,
    string PrimaryNameAttribute,
    bool HasNotes,
    bool HasActivities,
    bool ChangeTrackingEnabled);
