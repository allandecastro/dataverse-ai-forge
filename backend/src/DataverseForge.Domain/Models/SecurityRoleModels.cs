namespace DataverseForge.Domain.Models;

// Security role (Dataverse "role" entity) domain types. Phase 5 of the MDA
// rollout — the read tools are shared (UIBuilder + SolutionArchitect, and
// future Security Builder once that ships); the M:N writes
// (assign/unassign role to appmodule) live on UIBuilder. Role CRUD itself
// (create_role, update_role, delete_role) is intentionally NOT in this
// phase — that's the future Security Builder's territory.
//
// Reference:
//   role: https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/role

/// <summary>
/// One row in <c>get_security_roles</c>. Surfaces both managed (System
/// Administrator, Sales Person, …) and unmanaged custom roles — managed
/// roles are intentionally NOT excluded because most useful OOB roles are
/// managed and the agent will need to assign them to apps.
/// </summary>
public sealed record SecurityRoleInfo(
    Guid RoleId,
    string Name,
    string? Description,
    Guid? BusinessUnitId,
    string? BusinessUnitName,
    bool IsManaged,
    bool IsAutoAssigned,
    string? AppliesTo,
    Guid? RoleTemplateId,
    DateTime? ModifiedOn);

/// <summary>
/// Detailed view of a single role — adds parent role hierarchy + the
/// customizable flag on top of <see cref="SecurityRoleInfo"/>. Used by
/// <c>get_security_role_details</c>.
/// </summary>
public sealed record SecurityRoleDetails(
    SecurityRoleInfo Info,
    Guid? ParentRoleId,
    bool IsCustomizable);

/// <summary>
/// Result returned by <c>verify_app_role_assignment_against_plan</c>. The
/// Tester reads the planId stored by <c>assign_app_role</c> /
/// <c>unassign_app_role</c>, calls <c>list_app_roles</c> to see the live
/// state, and reports PASS / FAIL + discrepancies.
/// </summary>
public sealed record AppRoleAssignmentVerificationResult(
    Guid AppId,
    Guid RoleId,
    string PlanId,
    string Verdict,
    IReadOnlyList<string> Discrepancies);
