namespace DataverseForge.Domain.Models;

// Model-driven app (Dataverse "appmodule" entity) domain types. Phase 1 of the
// MDA implementation is read-only — list apps, inspect one, see its components,
// sitemap, and assigned security roles. Writes land in subsequent phases.
//
// References:
//   appmodule:           https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/appmodule
//   sitemap:             https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/sitemap
//   RetrieveAppComponents action returns the union of component metadata records
//   that an app currently surfaces (entities, forms, views, charts, dashboards,
//   sitemap, …) — heterogeneous response branched by `@odata.type`.

/// <summary>
/// One row in <c>get_apps_list</c>. Subset of the <c>appmodule</c> schema.
/// </summary>
public sealed record AppModuleInfo(
    Guid AppModuleId,
    string Name,
    string UniqueName,
    string Description,
    string WebResourceId,
    int ClientType,
    int NavigationType,
    int StateCode,
    int StatusCode,
    bool IsManaged,
    DateTime? PublishedOn);

/// <summary>
/// Detailed view of a single app — adds the descriptor blob and configxml on
/// top of <see cref="AppModuleInfo"/>. Returned by <c>get_app_details</c>.
/// </summary>
public sealed record AppModuleDetails(
    Guid AppModuleId,
    string Name,
    string UniqueName,
    string Description,
    string WebResourceId,
    int ClientType,
    int NavigationType,
    int StateCode,
    int StatusCode,
    bool IsManaged,
    DateTime? PublishedOn,
    string? Descriptor,
    string? ConfigXml);

/// <summary>
/// Single component reference returned by <c>RetrieveAppComponents</c>. The
/// underlying response is heterogeneous (entities, forms, savedqueries,
/// dashboards, …); this record flattens them to a uniform shape that's safe
/// to surface to the agent.
/// </summary>
public sealed record AppModuleComponentRef(
    string ComponentKind,                 // "entity" | "systemform" | "savedquery" | "savedqueryvisualization" | "sitemap" | …
    Guid Id,
    string? Name,
    string? LogicalName);                 // populated for entities; null for non-entity components

/// <summary>
/// Sitemap attached to an app. <see cref="SiteMapXml"/> is the raw XML — Phase
/// 4 will add a structured parse + validation layer on top.
/// </summary>
public sealed record SiteMapInfo(
    Guid SiteMapId,
    string SiteMapName,
    string SiteMapNameUnique,
    string SiteMapXml,
    bool IsManaged);

/// <summary>
/// Security role assigned to an app via the <c>appmoduleroles</c> M:N table.
/// </summary>
public sealed record AppModuleRoleInfo(
    Guid RoleId,
    string Name,
    Guid? BusinessUnitId,
    bool IsManaged);

/// <summary>
/// Composite read returned by <c>get_app_full</c>. Mirrors the §8.4 shape of
/// the spec: app properties + components + sitemap + roles in one round-trip
/// (fan-out parallelized by Task.WhenAll inside the client).
/// </summary>
public sealed record AppModuleFullDetails(
    AppModuleDetails App,
    IReadOnlyList<AppModuleComponentRef> Components,
    SiteMapInfo? SiteMap,
    IReadOnlyList<AppModuleRoleInfo> Roles);

// ─── Phase 2: write plans / verify / validate ────────────────────────────────

/// <summary>
/// Plan returned by <c>plan_create_app</c>. Same plan-by-reference shape as
/// chart / dashboard plans — the OData JSON payload is stored server-side
/// under <see cref="PlanId"/>; the agent only quotes the planId at execute
/// time.
/// </summary>
public sealed record AppCreationPlan(
    string PlanId,
    string Name,
    string UniqueName,
    string Description,
    int ClientType,
    int NavigationType,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

/// <summary>
/// Plan returned by <c>plan_update_app</c>. Same shape as
/// <see cref="AppCreationPlan"/> plus the existing app's GUID.
/// </summary>
public sealed record AppUpdatePlan(
    string PlanId,
    Guid AppId,
    string Name,
    string Description,
    int ClientType,
    int NavigationType,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

/// <summary>
/// One issue from the bound <c>ValidateApp</c> action. Dataverse returns a
/// list of these describing why an app can't be opened (no sitemap, missing
/// component, broken reference, …). Surfaced verbatim to the agent.
/// </summary>
public sealed record AppValidationIssue(
    string Severity,                      // "Error" | "Warning" | "Info"
    string Message,
    string? ComponentName,
    Guid? ComponentId,
    int? ComponentType);

/// <summary>
/// Result of the bound <c>ValidateApp</c> action. <see cref="IsValid"/> is
/// true when the action returns no Error-severity issues.
/// </summary>
public sealed record AppValidationResult(
    Guid AppId,
    bool IsValid,
    IReadOnlyList<AppValidationIssue> Issues);

/// <summary>
/// Result of <c>verify_app_against_plan</c>. Same diff/verdict shape as
/// <c>VerifyDashboardAgainstPlan</c> — name / description / clienttype /
/// navigationtype compared against the plan's stored payload.
/// </summary>
public sealed record AppVerificationResult(
    Guid AppId,
    string PlanId,
    string Verdict,                       // "PASS" | "FAIL" | "PARTIAL"
    IReadOnlyList<string> Discrepancies);

// ─── Phase 4: SiteMap structured spec + plans ────────────────────────────────

/// <summary>
/// One Area in a SiteMap. Areas are the top-level navigation tabs in a
/// model-driven app (e.g. Sales / Service / Marketing). Each area contains
/// 0+ groups of subareas. Mirrors the SiteMap.xsd Area element.
/// </summary>
public sealed record SiteMapAreaSpec(
    string Id,
    string? Title,
    string? Icon,
    bool ShowGroups,
    IReadOnlyList<SiteMapGroupSpec> Groups);

/// <summary>
/// A Group inside a SiteMap Area. Groups are visual section dividers in the
/// area's nav pane.
/// </summary>
public sealed record SiteMapGroupSpec(
    string Id,
    string? Title,
    IReadOnlyList<SiteMapSubAreaSpec> SubAreas);

/// <summary>
/// A SubArea is a single entry in the nav pane (one entity, one URL, or one
/// custom page). Exactly ONE of <see cref="Entity"/>, <see cref="Url"/>, or
/// <see cref="PageId"/> should be supplied — the SiteMapXmlBuilder rejects
/// specs that supply more than one.
/// </summary>
public sealed record SiteMapSubAreaSpec(
    string Id,
    string? Title,
    string? Entity,                       // logical name of a table — most common
    string? Url,                          // arbitrary URL — for custom pages, dashboards, web resources
    string? Type,                         // optional: "Default" / "Web Resource" / etc.
    string? PageId,                       // for custom pages
    string? Icon,
    IReadOnlyList<SiteMapPrivilegeSpec> Privileges);

/// <summary>
/// Restricts a SubArea's visibility to users who have <see cref="Privilege"/>
/// on <see cref="Entity"/>. Optional — omit to show the subarea to everyone
/// who has the role assigned.
/// </summary>
public sealed record SiteMapPrivilegeSpec(string Entity, string Privilege);

/// <summary>
/// Top-level structured spec the agent supplies as <c>areasJson</c> on
/// <c>plan_create_sitemap</c>. The planner turns this into a validated XML
/// blob via <see cref="SiteMapXmlBuilder"/> + <see cref="XsdValidator"/>.
/// </summary>
public sealed record SiteMapLayoutSpec(IReadOnlyList<SiteMapAreaSpec> Areas);

/// <summary>
/// Plan returned by <c>plan_create_sitemap</c>. Same plan-by-reference shape
/// as the app/dashboard/chart plans — the OData JSON payload is stored
/// server-side under <see cref="PlanId"/>; the agent only quotes the planId.
/// </summary>
public sealed record SiteMapCreationPlan(
    string PlanId,
    string SiteMapName,
    string SiteMapNameUnique,
    string SiteMapXml,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

/// <summary>
/// Plan returned by <c>plan_update_sitemap</c>. Same shape as
/// <see cref="SiteMapCreationPlan"/> plus the existing sitemap's GUID.
/// </summary>
public sealed record SiteMapUpdatePlan(
    string PlanId,
    Guid SiteMapId,
    string SiteMapName,
    string SiteMapXml,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

/// <summary>
/// Result of <c>verify_sitemap_against_plan</c>. Diffs the live SiteMapXml
/// against the plan's stored XML (whitespace-normalized).
/// </summary>
public sealed record SiteMapVerificationResult(
    Guid SiteMapId,
    string PlanId,
    string Verdict,                       // "PASS" | "FAIL" | "PARTIAL"
    IReadOnlyList<string> Discrepancies,
    string? PlanSiteMapXml,
    string? ActualSiteMapXml);

/// <summary>
/// Tracks a sitemap created during the session, mirroring
/// <see cref="CreatedApp"/>'s shape so the sidebar can show what shipped.
/// </summary>
public sealed record CreatedSiteMap(
    Guid SiteMapId,
    string SiteMapNameUnique,
    Guid? AttachedToAppId,
    DateTime CreatedAt);

/// <summary>
/// One component to attach to / detach from an app via
/// <c>AddAppComponents</c> / <c>RemoveAppComponents</c>. <see cref="Kind"/> is
/// the friendly type name ("sitemap", "entity", "systemform", "savedquery",
/// "savedqueryvisualization", "appmodule") which the client maps to the
/// canonical <c>Microsoft.Dynamics.CRM.&lt;type&gt;</c> shape.
/// </summary>
public sealed record AppComponentRef(string Kind, Guid Id);

/// <summary>
/// JSON-friendly input shape for the agent on add/remove app component tools.
/// <see cref="Type"/> is one of: "table" / "entity" / "form" / "systemform" /
/// "view" / "savedquery" / "chart" / "savedqueryvisualization" / "dashboard"
/// / "sitemap" / "app" / "appmodule".
/// <see cref="Id"/> is:
///   - For "table" / "entity": the entity LOGICAL NAME (e.g. "account",
///     "eppc_gift"). The tool resolves the MetadataId before calling the
///     AddAppComponents action.
///   - For everything else: the row's primary-key GUID as a string.
/// </summary>
public sealed record AppComponentInput(string Type, string Id);

/// <summary>
/// Per-component result returned by add/remove tools so the agent can report
/// which components actually shipped vs which got rejected (e.g. a typo'd
/// table logical name).
/// </summary>
public sealed record AppComponentOutcome(
    string Type,
    string Id,
    bool Succeeded,
    string? Message);
