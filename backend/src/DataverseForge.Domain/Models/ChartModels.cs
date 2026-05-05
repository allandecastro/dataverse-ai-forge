namespace DataverseForge.Domain.Models;

// Chart (savedqueryvisualization) domain types. Phase 1 supports the chart
// types Microsoft documents in their public samples — see
// https://learn.microsoft.com/en-us/power-apps/developer/data-platform/sample-charts
// We're org-owned only (no UserQueryVisualization) — the agent always writes
// to savedqueryvisualization, which requires System Admin / Customizer.

/// <summary>
/// One row in <c>get_charts_list</c>. Mirrors the savedqueryvisualization row
/// schema we surface to the agent.
/// </summary>
public sealed record ChartInfo(
    Guid SavedQueryVisualizationId,
    string Name,
    string Description,
    string PrimaryEntityTypeCode,
    bool IsDefault,
    bool IsManaged);

/// <summary>
/// Result of <c>get_chart_details</c> — the actual XML blobs alongside the
/// metadata. Verifier reads this back to compare against the plan.
/// </summary>
public sealed record ChartDetails(
    Guid SavedQueryVisualizationId,
    string Name,
    string Description,
    string PrimaryEntityTypeCode,
    string DataDescriptionXml,
    string PresentationDescriptionXml,
    bool IsDefault,
    bool IsManaged);

/// <summary>
/// Plan returned by <c>plan_create_chart</c>. Both XML blobs are previewed so
/// the agent can show them to the user; the full OData payload is stored
/// server-side under <see cref="PlanId"/>.
/// </summary>
public sealed record ChartCreationPlan(
    string PlanId,
    string TableLogicalName,
    string ChartName,
    string ChartType,
    string Description,
    string DataDescriptionXml,
    string PresentationDescriptionXml,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

/// <summary>
/// Plan returned by <c>plan_update_chart</c>. Same shape as
/// <see cref="ChartCreationPlan"/> plus the existing chart's GUID.
/// </summary>
public sealed record ChartUpdatePlan(
    string PlanId,
    Guid ChartId,
    string TableLogicalName,
    string ChartName,
    string ChartType,
    string Description,
    string DataDescriptionXml,
    string PresentationDescriptionXml,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

/// <summary>
/// Result of <c>verify_chart_against_plan</c>. Same diff/verdict shape as
/// <c>VerifyTableAgainstPlan</c> so the Tester can render the same kind of
/// VERIFICATION REPORT.
/// </summary>
public sealed record ChartVerificationResult(
    Guid ChartId,
    string PlanId,
    string Verdict,                       // "PASS" | "FAIL" | "PARTIAL"
    IReadOnlyList<string> Discrepancies,
    string? PlanDataDescriptionXml,
    string? ActualDataDescriptionXml,
    string? PlanPresentationDescriptionXml,
    string? ActualPresentationDescriptionXml);
