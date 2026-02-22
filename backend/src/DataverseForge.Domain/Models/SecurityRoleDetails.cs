namespace DataverseForge.Domain.Models;

public record SecurityRoleDetails(
    Guid RoleId,
    string Name,
    bool IsManaged,
    IReadOnlyList<SecurityPrivilege> Privileges);

public record SecurityPrivilege(
    string EntityName,
    string PrivilegeName,
    string Depth);
