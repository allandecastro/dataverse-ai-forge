using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Plans an update to an existing org-owned dashboard. Same XML builder as
/// create — only the HTTP verb differs (PATCH systemforms({id})). Caller
/// passes the existing dashboardId from get_dashboards_list.
/// </summary>
public class PlanUpdateDashboardTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Generate a plan for UPDATING an existing org-owned Dataverse dashboard. "
        + "Returns a planId; pass it verbatim to execute_update_dashboard. "
        + "Use get_dashboards_list to find the dashboardId. tabsJson syntax matches plan_create_dashboard.")]
    public async Task<DashboardUpdatePlan> PlanUpdateDashboardAsync(
        [Description("The GUID of the existing dashboard to update (from get_dashboards_list)")] string dashboardId,
        [Description("New display name (pass the existing name if not renaming)")] string dashboardName,
        [Description("Description for the dashboard")] string description,
        [Description("Structured layout JSON — same shape as plan_create_dashboard")] string tabsJson,
        CancellationToken cancellationToken,
        [Description("Maximum total cells. Default 6.")] int maxControls = 6)
    {
        if (!Guid.TryParse(dashboardId, out var parsedId) || parsedId == Guid.Empty)
        {
            return Reject(Guid.Empty, dashboardName, description, "dashboardId must be a valid GUID from get_dashboards_list.");
        }

        if (string.IsNullOrWhiteSpace(tabsJson))
            return Reject(parsedId, dashboardName, description, "tabsJson is required.");

        DashboardLayoutSpec? spec;
        try
        {
            spec = JsonSerializer.Deserialize<DashboardLayoutSpec>(tabsJson, _jsonOptions);
        }
        catch (JsonException jx)
        {
            return Reject(parsedId, dashboardName, description, $"tabsJson is not valid JSON: {jx.Message}");
        }

        if (spec is null || spec.Tabs is null || spec.Tabs.Count == 0)
            return Reject(parsedId, dashboardName, description, "tabsJson parsed but contained no tabs.");

        // F-34: surface clamp transparently. Mirror of PlanCreateDashboard.
        var warnings = new List<string>();
        var clampedMaxControls = Math.Clamp(maxControls, 1, 20);
        if (clampedMaxControls != maxControls)
        {
            warnings.Add($"maxControls clamped from {maxControls} to {clampedMaxControls} — Dataverse's documented absolute cap is 20.");
        }

        var validationErrors = DashboardXmlBuilder.Validate(spec, clampedMaxControls);
        if (validationErrors.Count > 0)
        {
            return Reject(parsedId, dashboardName, description, "Layout invalid — " + string.Join(" ", validationErrors));
        }

        // F-27: verify viewIds + visualizationIds against the live environment.
        // Mirror of PlanCreateDashboard's check (same fabricated-GUID failure
        // mode hits update silently and renders an empty cell). One round-trip
        // per distinct entity, cached.
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
                        + $"Available views (sample): {sample}.");
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
                        + $"Available charts (sample): {sample}.");
                }
            }
        }

        if (idErrors.Count > 0)
        {
            return Reject(parsedId, dashboardName, description,
                "Dashboard references view/chart IDs that do not exist in this environment. "
                + string.Join(" | ", idErrors));
        }

        var formXml = DashboardXmlBuilder.Render(spec);

        // PATCH payload — drop type and isdefault on update so we don't risk
        // accidentally re-typing an existing dashboard.
        var payload = new Dictionary<string, object>
        {
            ["name"] = dashboardName,
            ["description"] = description ?? string.Empty,
            ["formxml"] = formXml
        };
        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        var planId = $"update_dashboard_{Guid.NewGuid():N}";
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new DashboardUpdatePlan(
            PlanId: planId,
            DashboardId: parsedId,
            DashboardName: dashboardName,
            Description: description ?? string.Empty,
            FormXml: formXml,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: PlanRisk.Orange);
    }

    private static DashboardUpdatePlan Reject(Guid id, string name, string description, string reason)
        => new(
            PlanId: $"rejected_dashboard_invalid_{Guid.NewGuid():N}",
            DashboardId: id,
            DashboardName: name ?? string.Empty,
            Description: description ?? string.Empty,
            FormXml: string.Empty,
            WebApiPayloadJson: string.Empty,
            Warnings: new List<string> { "REJECTED: " + reason }.AsReadOnly(),
            RiskLevel: PlanRisk.Red);

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
