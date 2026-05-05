namespace DataverseForge.Domain.Models;

// Dashboard (SystemForm with type=0) domain types. We support org-owned
// dashboards only — written to SystemForm, never UserForm — same posture as
// our chart support. UserForm dashboards are deferred (they're per-user and
// not solution components).
//
// Cell content kinds:
//   chart   — ChartGridMode=Chart; needs VisualizationId (savedqueryvisualization GUID)
//   grid    — ChartGridMode=Grid;  needs ViewId (savedquery GUID)
//   all     — ChartGridMode=All;   needs both (chart-on-grid)
//   iframe  — Url + optional Security/Scrolling/Border/PassParameters
//   webresource — WebResourceId (form-enabled web resource GUID)
//
// References:
//   https://learn.microsoft.com/en-us/power-apps/developer/data-platform/dashboards
//   https://learn.microsoft.com/en-us/power-apps/developer/data-platform/sample-dashboards

/// <summary>
/// One row in <c>get_dashboards_list</c>. Mirrors the systemform row schema
/// (subset filtered to dashboards via type=0).
/// </summary>
public sealed record DashboardInfo(
    Guid FormId,
    string Name,
    string Description,
    bool IsDefault,
    bool IsManaged);

/// <summary>
/// Result of <c>get_dashboard_definition</c> — the FormXML for inspection /
/// pre-update editing. Verifier reads this back to compare against a plan.
/// </summary>
public sealed record DashboardDefinition(
    Guid FormId,
    string Name,
    string Description,
    string FormXml,
    bool IsDefault,
    bool IsManaged);

/// <summary>
/// Plan returned by <c>plan_create_dashboard</c>. Same plan-by-reference
/// pattern as forms / charts — full OData payload stored server-side.
/// </summary>
public sealed record DashboardCreationPlan(
    string PlanId,
    string DashboardName,
    string Description,
    string FormXml,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

/// <summary>
/// Plan returned by <c>plan_update_dashboard</c>. Same shape as
/// <see cref="DashboardCreationPlan"/> plus the existing dashboard's GUID.
/// </summary>
public sealed record DashboardUpdatePlan(
    string PlanId,
    Guid DashboardId,
    string DashboardName,
    string Description,
    string FormXml,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

/// <summary>
/// Result of <c>verify_dashboard_against_plan</c>. Same verdict shape as
/// <see cref="ChartVerificationResult"/>.
/// </summary>
public sealed record DashboardVerificationResult(
    Guid DashboardId,
    string PlanId,
    string Verdict,                       // "PASS" | "FAIL" | "PARTIAL"
    IReadOnlyList<string> Discrepancies,
    string? PlanFormXml,
    string? ActualFormXml);

// ─── Dashboard layout spec (the JSON shape PlanCreateDashboard accepts) ───

/// <summary>
/// Top-level dashboard layout. The agent ships one of these as JSON via
/// <c>tabsJson</c>; the planner builds FormXML from it.
/// </summary>
public sealed record DashboardLayoutSpec(
    IReadOnlyList<DashboardTabSpec> Tabs);

public sealed record DashboardTabSpec(
    string Name,
    string? Label,
    bool VerticalLayout = true,
    bool Expanded = true,
    bool ShowLabel = true,
    IReadOnlyList<DashboardColumnSpec>? Columns = null);

public sealed record DashboardColumnSpec(
    string Width,                                 // "100%", "50%", etc.
    IReadOnlyList<DashboardSectionSpec> Sections);

public sealed record DashboardSectionSpec(
    string Name,
    string? Label,
    bool ShowLabel = false,
    bool ShowBar = false,
    int Columns = 3,                              // emitted as "111"/"11"/"1"
    IReadOnlyList<DashboardCellSpec>? Cells = null);

/// <summary>
/// Single dashboard cell. Cells are arranged into rows automatically by the
/// planner — every cell becomes <c>row[0]</c> with N-1 trailing empty rows
/// where N = <see cref="RowSpan"/>, matching the dashboard FormXML shape
/// Microsoft's samples use.
/// </summary>
public sealed record DashboardCellSpec(
    string CellType,                              // "chart" | "grid" | "all" | "iframe" | "webresource"
    int ColSpan = 1,
    int RowSpan = 12,
    string? Label = null,
    bool ShowLabel = false,
    // Chart / Grid / All — savedquery + savedqueryvisualization references
    string? TargetEntityType = null,              // logical name (e.g. "opportunity")
    string? ViewId = null,                        // savedquery GUID
    string? VisualizationId = null,               // savedqueryvisualization GUID
    bool IsUserView = false,
    bool IsUserChart = false,
    bool EnableQuickFind = false,
    bool EnableViewPicker = false,
    bool EnableJumpBar = false,
    bool EnableChartPicker = false,
    int RecordsPerPage = 10,
    string AutoExpand = "Fixed",                  // dashboards must be Fixed
    // IFRAME — Url is REQUIRED; security restrictions apply per type
    string? IframeUrl = null,
    bool IframeRestrictCrossFrameScripting = true,
    bool IframeScrolling = false,
    bool IframeBorder = false,
    bool IframePassParameters = false,
    // Web resource
    string? WebResourceId = null,
    string? WebResourceUrl = null);
