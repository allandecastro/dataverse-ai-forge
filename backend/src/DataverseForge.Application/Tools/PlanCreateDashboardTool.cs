using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Plans creation of an org-owned Dataverse dashboard (systemform with type=0).
/// Builds the FormXML from a structured tabsJson spec, validates against the
/// known dashboard limitations (each tab needs a section, AutoExpand=Fixed,
/// IFRAME needs a Url, max 6 controls by default), and stores the OData
/// payload server-side under a planId.
///
/// User-owned dashboards (UserForm) are NOT supported — same posture as charts.
/// </summary>
public class PlanCreateDashboardTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Generate a plan for creating a Dataverse dashboard (org-owned, systemform with type=0). "
        + "Returns a planId, the FormXML preview, warnings, and risk level. The OData JSON payload is "
        + "stored server-side under planId — pass it verbatim to execute_create_dashboard after approval. "
        + "User-owned dashboards (UserForm) are NOT supported. "
        + "tabsJson is a JSON object: {tabs:[{name,label?,verticalLayout?,expanded?,showLabel?,"
        + "columns:[{width,sections:[{name,label?,showLabel?,showBar?,columns?,cells:[{cellType,colSpan?,rowSpan?,"
        + "label?,showLabel?,targetEntityType,viewId?,visualizationId?,isUserView?,isUserChart?,enableQuickFind?,"
        + "enableViewPicker?,enableJumpBar?,enableChartPicker?,recordsPerPage?,autoExpand?,"
        + "iframeUrl?,iframeRestrictCrossFrameScripting?,iframeScrolling?,iframeBorder?,iframePassParameters?,"
        + "webResourceId?,webResourceUrl?}]}]}]}]}. "
        + "cellType: 'chart' (chart only), 'grid' (grid only), 'all' (chart+grid), 'iframe', 'webresource'. "
        + "Default control cap is 6 cells; raise via maxControls if your tenant allows more.")]
    public async Task<DashboardCreationPlan> PlanCreateDashboardAsync(
        [Description("The dashboard's display name (e.g., 'Sales Performance Dashboard')")] string dashboardName,
        [Description("Description for the dashboard (free text)")] string description,
        [Description("Structured layout JSON (see tool description for the shape).")] string tabsJson,
        CancellationToken cancellationToken,
        [Description("Maximum total cells across the dashboard. Microsoft default is 6; the platform allows up to 20 if a tenant admin has raised it.")]
        int maxControls = 6)
    {
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(dashboardName))
            return Reject(dashboardName, description, "dashboardName is required.");

        if (string.IsNullOrWhiteSpace(tabsJson))
            return Reject(dashboardName, description, "tabsJson is required — pass at least one tab with one section and one cell.");

        DashboardLayoutSpec? spec;
        try
        {
            spec = JsonSerializer.Deserialize<DashboardLayoutSpec>(tabsJson, _jsonOptions);
        }
        catch (JsonException jx)
        {
            return Reject(dashboardName, description,
                $"tabsJson is not valid JSON: {jx.Message}. See tool description for the expected shape.");
        }

        if (spec is null || spec.Tabs is null || spec.Tabs.Count == 0)
            return Reject(dashboardName, description, "tabsJson parsed but contained no tabs.");

        var validationErrors = DashboardXmlBuilder.Validate(spec, Math.Clamp(maxControls, 1, 20));
        if (validationErrors.Count > 0)
        {
            return Reject(dashboardName, description, "Layout invalid — " + string.Join(" ", validationErrors));
        }

        // ─── Verify viewIds + visualizationIds against the live environment ──
        // Real session 2026-05-05: agent fabricated a view GUID
        // (1ec44c71-…3ded) and the dashboard create looped 4× failing with
        // opaque "SavedQuery does not exist" errors. Cheap fix: list views /
        // charts per target entity once and reject the plan if any cell
        // references an ID the environment doesn't have. Caches per-entity
        // lookups so we only pay the round-trip once per distinct entity.
        var viewsByEntity = new Dictionary<string, IReadOnlyList<ViewInfo>>(StringComparer.Ordinal);
        var chartsByEntity = new Dictionary<string, IReadOnlyList<ChartInfo>>(StringComparer.Ordinal);
        var idErrors = new List<string>();

        foreach (var (cell, cellRef) in EnumerateCells(spec))
        {
            var type = (cell.CellType ?? "").ToLowerInvariant();
            if (type is not ("chart" or "grid" or "all")) continue;
            var target = (cell.TargetEntityType ?? "").ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(target)) continue;

            if (!string.IsNullOrWhiteSpace(cell.ViewId) && Guid.TryParse(cell.ViewId, out var viewGuid) && viewGuid != Guid.Empty)
            {
                if (!viewsByEntity.TryGetValue(target, out var views))
                {
                    try
                    {
                        views = await dataverseClient.GetViewsListAsync(target, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        warnings.Add($"Could not list views for '{target}' to verify viewId — proceeding without verification. Underlying: {ex.Message}");
                        views = Array.Empty<ViewInfo>();
                    }
                    viewsByEntity[target] = views;
                }
                if (views.Count > 0 && !views.Any(v => v.ViewId == viewGuid))
                {
                    var sample = string.Join(", ", views.Take(5).Select(v => $"{v.Name} ({v.ViewId})"));
                    idErrors.Add($"{cellRef}.viewId '{viewGuid}' does not exist on '{target}'. "
                        + $"Available views (sample): {sample}. Call get_views_list to pick a real one before retrying.");
                }
            }

            if (!string.IsNullOrWhiteSpace(cell.VisualizationId) && Guid.TryParse(cell.VisualizationId, out var visGuid) && visGuid != Guid.Empty)
            {
                if (!chartsByEntity.TryGetValue(target, out var charts))
                {
                    try
                    {
                        charts = await dataverseClient.GetChartsListAsync(target, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        warnings.Add($"Could not list charts for '{target}' to verify visualizationId — proceeding without verification. Underlying: {ex.Message}");
                        charts = Array.Empty<ChartInfo>();
                    }
                    chartsByEntity[target] = charts;
                }
                if (charts.Count > 0 && !charts.Any(c => c.SavedQueryVisualizationId == visGuid))
                {
                    var sample = string.Join(", ", charts.Take(5).Select(c => $"{c.Name} ({c.SavedQueryVisualizationId})"));
                    idErrors.Add($"{cellRef}.visualizationId '{visGuid}' does not exist on '{target}'. "
                        + $"Available charts (sample): {sample}. Call get_charts_list to pick a real one before retrying.");
                }
            }
        }

        if (idErrors.Count > 0)
        {
            return Reject(dashboardName, description,
                "Dashboard references view/chart IDs that do not exist in this environment. "
                + string.Join(" | ", idErrors));
        }

        var formXml = DashboardXmlBuilder.Render(spec);

        // Build the OData payload. Dashboards: type=0, isdefault=false (we
        // never ship a new dashboard as the org default), no objecttypecode
        // (dashboards aren't bound to a single entity).
        var payload = new Dictionary<string, object>
        {
            ["name"] = dashboardName,
            ["description"] = description ?? string.Empty,
            ["formxml"] = formXml,
            ["type"] = 0,
            ["isdefault"] = false,
            // Dashboards must explicitly set objecttypecode = "none" — Dataverse
            // rejects null and rejects entity-bound values for dashboards.
            ["objecttypecode"] = "none"
        };
        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        // Risk level: dashboards are a UI artifact only — orange when there
        // are >3 cells (more components → higher chance of layout surprises).
        var totalCells = spec.Tabs.Sum(t => (t.Columns ?? Array.Empty<DashboardColumnSpec>())
            .Sum(c => c.Sections.Sum(s => (s.Cells ?? Array.Empty<DashboardCellSpec>()).Count)));
        if (totalCells > 4) warnings.Add($"Dashboard has {totalCells} cells — review the preview before approval.");

        var riskLevel = totalCells > 4 ? "orange" : "yellow";

        var planId = $"create_dashboard_{Guid.NewGuid():N}";
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new DashboardCreationPlan(
            PlanId: planId,
            DashboardName: dashboardName,
            Description: description ?? string.Empty,
            FormXml: formXml,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: riskLevel);
    }

    private static DashboardCreationPlan Reject(string name, string description, string reason)
        => new(
            PlanId: $"rejected_dashboard_invalid_{Guid.NewGuid():N}",
            DashboardName: name ?? string.Empty,
            Description: description ?? string.Empty,
            FormXml: string.Empty,
            WebApiPayloadJson: string.Empty,
            Warnings: new List<string> { "REJECTED: " + reason }.AsReadOnly(),
            RiskLevel: "red");

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static IEnumerable<(DashboardCellSpec Cell, string CellRef)> EnumerateCells(DashboardLayoutSpec spec)
    {
        for (var ti = 0; ti < spec.Tabs.Count; ti++)
        {
            var tab = spec.Tabs[ti];
            var columns = tab.Columns ?? Array.Empty<DashboardColumnSpec>();
            for (var ci = 0; ci < columns.Count; ci++)
            {
                var col = columns[ci];
                for (var si = 0; si < col.Sections.Count; si++)
                {
                    var section = col.Sections[si];
                    var cells = section.Cells ?? Array.Empty<DashboardCellSpec>();
                    for (var ki = 0; ki < cells.Count; ki++)
                    {
                        yield return (cells[ki], $"tabs[{ti}].columns[{ci}].sections[{si}].cells[{ki}]");
                    }
                }
            }
        }
    }
}
