namespace DataverseForge.Domain.Models;

public record SecurityRoleCreationPlan(
    string RoleName,
    string Description,
    Guid? CopyFromRoleId,
    IReadOnlyList<RolePrivilegeSpec> Privileges,
    string PrivilegeMatrix,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

public record RolePrivilegeSpec(
    string TableName,
    string? Create,
    string? Read,
    string? Write,
    string? Delete,
    string? Append,
    string? AppendTo,
    string? Assign,
    string? Share);
