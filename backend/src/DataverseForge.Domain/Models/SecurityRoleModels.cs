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

// ── Security Builder agent — full role CRUD (Phase 6) ────────────────────
//
// The Security Builder owns role CRUD (create / update / delete), distinct
// from Phase 5's read-only role tools. v1 scope is the role record + the
// privilege matrix; field-level security profiles + column security
// (IsSecured flag on attributes) stay deferred.
//
// Reference docs:
//   role:                 https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/role
//   privilege:            https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/privilege
//   AddPrivilegesRole:    https://learn.microsoft.com/power-apps/developer/data-platform/webapi/reference/addprivilegesrole
//   RemovePrivilegeRole:  https://learn.microsoft.com/power-apps/developer/data-platform/webapi/reference/removeprivilegerole
//
// Naming: "RolePrivilegeSpec" is what the AGENT supplies (table + operation +
// depth, human-friendly). "RolePrivilegeAssignment" is what Dataverse takes
// over the wire (privilegeId GUID + numeric depth). Plan tools resolve the
// former into the latter via get_privileges_for_table.

/// <summary>
/// One human-friendly privilege ask. The planner resolves
/// <see cref="TableLogicalName"/> + <see cref="Operation"/> to a real
/// privilege GUID via <c>get_privileges_for_table</c>, then verifies the
/// requested <see cref="Depth"/> is allowed on that privilege.
/// </summary>
/// <param name="TableLogicalName">Lowercase logical name (e.g. <c>account</c>, <c>adc_feedback</c>).</param>
/// <param name="Operation">One of: Create, Read, Write, Delete, Append, AppendTo, Assign, Share.</param>
/// <param name="Depth">One of: User (Basic), BU (Local), ParentChild (Deep), Org (Global).</param>
public sealed record RolePrivilegeSpec(
    string TableLogicalName,
    string Operation,
    string Depth);

/// <summary>
/// Wire-shape privilege assignment — what
/// <c>AddPrivilegesRole</c> / <c>RemovePrivilegeRole</c> take and what the
/// <c>roleprivileges_association</c> read returns. The planner produces these
/// from <see cref="RolePrivilegeSpec"/>; Tester reads them directly when
/// diffing the live role.
/// </summary>
/// <param name="PrivilegeId">Real Dataverse privilege GUID.</param>
/// <param name="Depth">One of: Basic, Local, Deep, Global. Wire form Dataverse expects.</param>
/// <param name="DebugName">Optional privilege.Name for logging / discrepancies (e.g. <c>prvReadAccount</c>). Not sent over the wire.</param>
public sealed record RolePrivilegeAssignment(
    Guid PrivilegeId,
    string Depth,
    string? DebugName = null);

/// <summary>
/// Plan record returned by <c>plan_create_security_role</c>. The composite
/// payload (role POST body + AddPrivilegesRole action body) is staged
/// server-side under <see cref="PlanId"/>; the agent only needs to pass the
/// planId to <c>execute_create_security_role</c>.
/// </summary>
public sealed record RoleCreationPlan(
    string PlanId,
    string Name,
    string Description,
    Guid BusinessUnitId,
    string? BusinessUnitName,
    IReadOnlyList<RolePrivilegeAssignment> Privileges,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

/// <summary>
/// Plan record returned by <c>plan_update_security_role</c>. Stores the
/// privilege DELTA (add + remove lists), not the full target spec — the
/// planner reads the live role first and computes what to change. Optional
/// rename via <see cref="NewName"/> / <see cref="NewDescription"/>.
/// </summary>
public sealed record RoleUpdatePlan(
    string PlanId,
    Guid RoleId,
    string? NewName,
    string? NewDescription,
    IReadOnlyList<RolePrivilegeAssignment> PrivilegesToAdd,
    IReadOnlyList<RolePrivilegeAssignment> PrivilegesToRemove,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

/// <summary>
/// Result of <c>verify_security_role_against_plan</c>. Tester routes by the
/// planId prefix (<c>create_security_role_*</c> vs
/// <c>update_security_role_*</c>) and produces the same record either way.
/// </summary>
public sealed record RoleVerificationResult(
    Guid RoleId,
    string PlanId,
    string Verdict,
    IReadOnlyList<Discrepancy> Discrepancies);

/// <summary>
/// One row from <c>get_business_units</c>. Surfaces the parent BU id so the
/// planner can present a tree view to the user when there's ambiguity over
/// which BU to bind a new role to.
/// </summary>
public sealed record BusinessUnitInfo(
    Guid BusinessUnitId,
    string Name,
    Guid? ParentBusinessUnitId);

/// <summary>
/// One privilege row returned by <c>get_privileges_for_table</c>. The four
/// CanBe* flags map to the four <c>PrivilegeDepth</c> tiers; the planner
/// uses them to refuse a depth that Dataverse won't accept on this
/// privilege (e.g. asking for Global on a privilege that's user-only).
/// </summary>
public sealed record PrivilegeMetadata(
    Guid PrivilegeId,
    string Name,
    string AccessRight,
    bool CanBeBasic,
    bool CanBeLocal,
    bool CanBeDeep,
    bool CanBeGlobal);

/// <summary>
/// Wraps <see cref="PrivilegeMetadata"/> reads with success/error fields so
/// agent tools can surface Dataverse failures verbatim instead of throwing
/// (which would surface as a generic "Function failed." to the agent and
/// strip the actual error body).
/// </summary>
public sealed record PrivilegesForTableResult(
    bool Success,
    string? ErrorMessage,
    string TableLogicalName,
    IReadOnlyList<PrivilegeMetadata> Privileges);

/// <summary>
/// One row from <c>get_role_templates</c>. Templates mark a role so Dataverse
/// auto-replicates it into every new business unit as the unit is created —
/// useful for multi-org tenants. Setting <see cref="RoleTemplateId"/> on a
/// new role via plan_create_security_role's optional parameter binds the
/// role to the template at create time.
/// </summary>
/// <remarks>
/// Reference: https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/roletemplate
/// </remarks>
public sealed record RoleTemplateInfo(
    Guid RoleTemplateId,
    string Name,
    string? Description);

/// <summary>
/// Plan record returned by <c>plan_grant_privilege_to_roles</c>. Grants a
/// single (privilege, depth) pair to N roles at once — useful for "give
/// Sales User + Sales Manager + Sales VP Read on Account" without three
/// separate update plans.
/// </summary>
public sealed record BulkPrivilegeGrantPlan(
    string PlanId,
    Guid PrivilegeId,
    string PrivilegeName,
    string AccessRight,
    string Depth,
    IReadOnlyList<Guid> TargetRoleIds,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

/// <summary>
/// Result returned by <c>verify_bulk_privilege_grant_against_plan</c>. The
/// composite verdict (<c>OverallVerdict</c>) is PASS only if every per-role
/// result is PASS; otherwise FAIL with the per-role discrepancies inline.
/// </summary>
public sealed record BulkPrivilegeGrantVerificationResult(
    string PlanId,
    string OverallVerdict,
    IReadOnlyList<RoleVerificationResult> PerRoleResults);
