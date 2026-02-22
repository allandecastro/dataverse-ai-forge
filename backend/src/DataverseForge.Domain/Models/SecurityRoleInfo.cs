namespace DataverseForge.Domain.Models;

public record SecurityRoleInfo(
    Guid RoleId,
    string Name,
    bool IsManaged,
    bool IsCustomizable,
    Guid BusinessUnitId,
    int PrivilegeCount);
