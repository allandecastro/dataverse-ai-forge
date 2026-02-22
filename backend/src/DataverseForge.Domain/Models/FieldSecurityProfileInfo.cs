namespace DataverseForge.Domain.Models;

public record FieldSecurityProfileInfo(
    Guid ProfileId,
    string Name,
    IReadOnlyList<FieldPermission> FieldPermissions);

public record FieldPermission(
    string TableLogicalName,
    string FieldLogicalName,
    bool CanRead,
    bool CanUpdate,
    bool CanCreate);
