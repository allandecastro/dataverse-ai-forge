using System.Text;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Builds the FormXML for a Dataverse dashboard (systemform with type=0).
/// Dashboards reuse the form schema's tab/section/row/cell skeleton but
/// don't have <c>&lt;header&gt;</c>, <c>&lt;footer&gt;</c>, <c>&lt;events&gt;</c>,
/// <c>&lt;formLibraries&gt;</c>, <c>&lt;DisplayConditions&gt;</c>, or
/// <c>&lt;ancestor&gt;</c> — those are form-only. Cells contain controls
/// (chart, grid, IFRAME, web resource), never field bindings.
///
/// Reference shapes: see Microsoft's sample-dashboards article.
/// </summary>
public static class DashboardXmlBuilder
{
    // Subgrid / chart cell — same classid as form subgrids.
    public const string ChartGridClassId = "{E7A81278-8635-4D9E-8D4D-59480B391C5B}";
    // IFRAME control on a dashboard.
    public const string IframeClassId = "{8C54D6F7-4914-4505-9D8A-9A9D227B5A5C}";
    // HTML / web resource control.
    public const string WebResourceClassId = "{9FDF5F91-88B1-47F4-AD53-C11EFC01A01D}";

    public static readonly string[] SupportedCellTypes =
        ["chart", "grid", "all", "iframe", "webresource"];

    /// <summary>
    /// Validate a parsed layout. Returns a list of REJECTED reasons; an empty
    /// list means the layout is good.
    /// </summary>
    public static List<string> Validate(DashboardLayoutSpec spec, int maxControls = 6)
    {
        var errors = new List<string>();

        if (spec.Tabs.Count == 0)
        {
            errors.Add("Dashboard must have at least one tab.");
            return errors;
        }

        var totalCells = 0;
        for (var ti = 0; ti < spec.Tabs.Count; ti++)
        {
            var tab = spec.Tabs[ti];
            var tabRef = $"tabs[{ti}]";
            if (string.IsNullOrWhiteSpace(tab.Name))
                errors.Add($"{tabRef}.name is required.");

            var columns = tab.Columns ?? Array.Empty<DashboardColumnSpec>();
            if (columns.Count == 0)
            {
                errors.Add($"{tabRef} must have at least one column.");
                continue;
            }

            for (var ci = 0; ci < columns.Count; ci++)
            {
                var col = columns[ci];
                var colRef = $"{tabRef}.columns[{ci}]";
                if (string.IsNullOrWhiteSpace(col.Width))
                    errors.Add($"{colRef}.width is required (e.g. \"100%\", \"50%\").");

                if (col.Sections.Count == 0)
                {
                    errors.Add($"{colRef} must have at least one section. (Dashboards reject tabs without sections.)");
                    continue;
                }

                for (var si = 0; si < col.Sections.Count; si++)
                {
                    var section = col.Sections[si];
                    var secRef = $"{colRef}.sections[{si}]";
                    if (string.IsNullOrWhiteSpace(section.Name))
                        errors.Add($"{secRef}.name is required.");
                    if (section.Columns is < 1 or > 4)
                        errors.Add($"{secRef}.columns must be between 1 and 4.");

                    var cells = section.Cells ?? Array.Empty<DashboardCellSpec>();
                    foreach (var (cell, idx) in cells.Select((c, i) => (c, i)))
                    {
                        ValidateCell(cell, $"{secRef}.cells[{idx}]", errors);
                        totalCells++;
                    }
                }
            }
        }

        if (totalCells > maxControls)
            errors.Add($"Dashboard has {totalCells} controls but the platform default cap is {maxControls}. "
                + "Reduce the cell count or ask the maker portal admin to raise MaximumControlsLimit (max 20).");

        return errors;
    }

    private static void ValidateCell(DashboardCellSpec cell, string cellRef, List<string> errors)
    {
        var type = (cell.CellType ?? "").ToLowerInvariant();
        if (!SupportedCellTypes.Contains(type))
        {
            errors.Add($"{cellRef}.cellType '{cell.CellType}' is invalid; must be one of: {string.Join(", ", SupportedCellTypes)}.");
            return;
        }

        if (cell.RowSpan < 1)
            errors.Add($"{cellRef}.rowSpan must be >= 1.");
        if (cell.ColSpan < 1)
            errors.Add($"{cellRef}.colSpan must be >= 1.");

        switch (type)
        {
            case "chart":
            case "grid":
            case "all":
                if (string.IsNullOrWhiteSpace(cell.TargetEntityType))
                    errors.Add($"{cellRef}.targetEntityType is required for chart/grid/all cells.");
                if (type is "chart" or "all" && string.IsNullOrWhiteSpace(cell.VisualizationId))
                    errors.Add($"{cellRef}.visualizationId is required for {type} cells (call get_charts_list to pick one).");
                // ViewId is required for ALL three modes — even chart-only cells
                // need a view because the chart visualization runs the view's
                // FetchXML to pull rows before aggregating. Without it the
                // dashboard renders empty (real session 2026-05-05: agent built
                // chart-only cells with no ViewId, dashboard was visually empty).
                if (string.IsNullOrWhiteSpace(cell.ViewId))
                    errors.Add($"{cellRef}.viewId is required for {type} cells "
                        + "(charts run a view's FetchXML to source data — without ViewId the cell renders empty). "
                        + "Call get_views_list and pass a Public view's savedqueryid.");
                if (!string.Equals(cell.AutoExpand, "Fixed", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"{cellRef}.autoExpand must be 'Fixed' for dashboard cells. Microsoft rejects 'Auto' on dashboards.");
                if (cell.RecordsPerPage is < 1 or > 250)
                    errors.Add($"{cellRef}.recordsPerPage must be between 1 and 250.");
                if (!string.IsNullOrWhiteSpace(cell.ViewId) && !Guid.TryParse(cell.ViewId, out var vGuid))
                    errors.Add($"{cellRef}.viewId '{cell.ViewId}' is not a valid GUID.");
                else if (!string.IsNullOrWhiteSpace(cell.ViewId) && Guid.TryParse(cell.ViewId, out var vg2) && vg2 == Guid.Empty)
                    errors.Add($"{cellRef}.viewId is the all-zero GUID — Dataverse rejects it. Use get_views_list to pick a real view.");
                if (!string.IsNullOrWhiteSpace(cell.VisualizationId) && !Guid.TryParse(cell.VisualizationId, out var visGuid))
                    errors.Add($"{cellRef}.visualizationId '{cell.VisualizationId}' is not a valid GUID.");
                else if (!string.IsNullOrWhiteSpace(cell.VisualizationId) && Guid.TryParse(cell.VisualizationId, out var vg3) && vg3 == Guid.Empty)
                    errors.Add($"{cellRef}.visualizationId is the all-zero GUID — Dataverse rejects it.");
                break;
            case "iframe":
                if (string.IsNullOrWhiteSpace(cell.IframeUrl))
                    errors.Add($"{cellRef}.iframeUrl is required for iframe cells. Microsoft rejects IFRAME cells without a Url.");
                break;
            case "webresource":
                if (string.IsNullOrWhiteSpace(cell.WebResourceId))
                    errors.Add($"{cellRef}.webResourceId is required for webresource cells.");
                else if (!Guid.TryParse(cell.WebResourceId, out var wrGuid))
                    errors.Add($"{cellRef}.webResourceId '{cell.WebResourceId}' is not a valid GUID.");
                else if (wrGuid == Guid.Empty)
                    errors.Add($"{cellRef}.webResourceId is the all-zero GUID — Dataverse rejects it.");
                break;
        }
    }

    /// <summary>
    /// Render the dashboard FormXML. Caller is responsible for validating
    /// (call <see cref="Validate"/> first).
    /// </summary>
    public static string Render(DashboardLayoutSpec spec)
    {
        var xml = new StringBuilder();
        xml.AppendLine("<form>");
        xml.AppendLine("  <tabs>");
        foreach (var tab in spec.Tabs)
        {
            EmitTab(xml, tab);
        }
        xml.AppendLine("  </tabs>");
        xml.AppendLine("</form>");
        return xml.ToString();
    }

    private static void EmitTab(StringBuilder xml, DashboardTabSpec tab)
    {
        var tabId = NewGuid();
        var tabName = string.IsNullOrWhiteSpace(tab.Name) ? tabId : tab.Name;
        var label = string.IsNullOrWhiteSpace(tab.Label) ? tabName : tab.Label!;

        var attrs = new List<string>
        {
            $"showlabel=\"{(tab.ShowLabel ? "true" : "false")}\"",
            $"verticallayout=\"{(tab.VerticalLayout ? "true" : "false")}\"",
            $"id=\"{tabId}\"",
            $"name=\"{XmlEscape(tabName)}\"",
            "locklevel=\"0\"",
            $"expanded=\"{(tab.Expanded ? "true" : "false")}\"",
        };
        xml.AppendLine($"    <tab {string.Join(' ', attrs)}>");
        xml.AppendLine($"      <labels><label description=\"{XmlEscape(label)}\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
        xml.AppendLine("      <columns>");

        var columns = tab.Columns ?? Array.Empty<DashboardColumnSpec>();
        foreach (var col in columns)
        {
            xml.AppendLine($"        <column width=\"{col.Width}\">");
            xml.AppendLine("          <sections>");
            foreach (var section in col.Sections)
            {
                EmitSection(xml, section);
            }
            xml.AppendLine("          </sections>");
            xml.AppendLine("        </column>");
        }
        xml.AppendLine("      </columns>");
        xml.AppendLine("    </tab>");
    }

    private static void EmitSection(StringBuilder xml, DashboardSectionSpec section)
    {
        var sectionId = NewGuid();
        var sectionName = string.IsNullOrWhiteSpace(section.Name) ? sectionId : section.Name;
        var label = string.IsNullOrWhiteSpace(section.Label) ? sectionName : section.Label!;
        // columns="111" / "11" / "1" — one '1' per cell column track
        var clamped = Math.Clamp(section.Columns, 1, 4);
        var columnsAttr = new string('1', clamped);

        var attrs = new List<string>
        {
            $"showlabel=\"{(section.ShowLabel ? "true" : "false")}\"",
            $"showbar=\"{(section.ShowBar ? "true" : "false")}\"",
            $"columns=\"{columnsAttr}\"",
            $"id=\"{sectionId}\"",
            $"name=\"{XmlEscape(sectionName)}\"",
        };
        xml.AppendLine($"            <section {string.Join(' ', attrs)}>");
        xml.AppendLine($"              <labels><label description=\"{XmlEscape(label)}\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
        xml.AppendLine("              <rows>");

        var cells = section.Cells ?? Array.Empty<DashboardCellSpec>();
        if (cells.Count == 0)
        {
            // Empty section — emit one empty row so Dataverse accepts it.
            xml.AppendLine("                <row></row>");
        }
        else
        {
            // First row contains all the cells; trailing empty rows fill out
            // the rowspan so the layout matches Microsoft's sample shape.
            xml.AppendLine("                <row>");
            foreach (var cell in cells)
            {
                EmitCell(xml, cell);
            }
            xml.AppendLine("                </row>");

            var maxRowSpan = cells.Max(c => Math.Max(1, c.RowSpan));
            for (var i = 1; i < maxRowSpan; i++)
            {
                xml.AppendLine("                <row></row>");
            }
        }

        xml.AppendLine("              </rows>");
        xml.AppendLine("            </section>");
    }

    private static void EmitCell(StringBuilder xml, DashboardCellSpec cell)
    {
        var cellId = NewGuid();
        var label = string.IsNullOrWhiteSpace(cell.Label) ? cell.CellType : cell.Label!;

        var cellAttrs = new List<string>
        {
            $"colspan=\"{Math.Max(1, cell.ColSpan)}\"",
            $"rowspan=\"{Math.Max(1, cell.RowSpan)}\"",
            $"showlabel=\"{(cell.ShowLabel ? "true" : "false")}\"",
            $"id=\"{cellId}\"",
            "auto=\"false\"",
        };
        xml.AppendLine($"                  <cell {string.Join(' ', cellAttrs)}>");
        xml.AppendLine($"                    <labels><label description=\"{XmlEscape(label!)}\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");

        var type = (cell.CellType ?? "").ToLowerInvariant();
        var controlSlug = NewControlSlug(type);
        switch (type)
        {
            case "chart":
                EmitChartGridControl(xml, controlSlug, cell, "Chart");
                break;
            case "grid":
                EmitChartGridControl(xml, controlSlug, cell, "Grid");
                break;
            case "all":
                EmitChartGridControl(xml, controlSlug, cell, "All");
                break;
            case "iframe":
                EmitIframeControl(xml, controlSlug, cell);
                break;
            case "webresource":
                EmitWebResourceControl(xml, controlSlug, cell);
                break;
            default:
                // Validation should have caught this — emit a stub so the XML
                // remains well-formed even on the unhappy path.
                xml.AppendLine($"                    <control id=\"{controlSlug}\" classid=\"{ChartGridClassId}\"><parameters /></control>");
                break;
        }

        xml.AppendLine("                  </cell>");
    }

    private static void EmitChartGridControl(StringBuilder xml, string controlSlug, DashboardCellSpec cell, string mode)
    {
        var target = (cell.TargetEntityType ?? "").ToLowerInvariant();
        xml.AppendLine($"                    <control id=\"{controlSlug}\" classid=\"{ChartGridClassId}\">");
        xml.AppendLine("                      <parameters>");
        xml.AppendLine($"                        <TargetEntityType>{XmlEscape(target)}</TargetEntityType>");
        xml.AppendLine($"                        <ChartGridMode>{mode}</ChartGridMode>");
        xml.AppendLine($"                        <EnableQuickFind>{(cell.EnableQuickFind ? "true" : "false")}</EnableQuickFind>");
        xml.AppendLine($"                        <EnableViewPicker>{(cell.EnableViewPicker ? "true" : "false")}</EnableViewPicker>");
        xml.AppendLine($"                        <EnableJumpBar>{(cell.EnableJumpBar ? "true" : "false")}</EnableJumpBar>");
        xml.AppendLine($"                        <RecordsPerPage>{cell.RecordsPerPage}</RecordsPerPage>");
        if (!string.IsNullOrWhiteSpace(cell.ViewId))
            xml.AppendLine($"                        <ViewId>{{{XmlEscape(cell.ViewId.Trim('{', '}'))}}}</ViewId>");
        xml.AppendLine($"                        <IsUserView>{(cell.IsUserView ? "true" : "false")}</IsUserView>");
        xml.AppendLine("                        <ViewIds></ViewIds>");
        xml.AppendLine($"                        <AutoExpand>{XmlEscape(cell.AutoExpand)}</AutoExpand>");
        if (!string.IsNullOrWhiteSpace(cell.VisualizationId))
            xml.AppendLine($"                        <VisualizationId>{{{XmlEscape(cell.VisualizationId.Trim('{', '}'))}}}</VisualizationId>");
        xml.AppendLine($"                        <IsUserChart>{(cell.IsUserChart ? "true" : "false")}</IsUserChart>");
        xml.AppendLine($"                        <EnableChartPicker>{(cell.EnableChartPicker ? "true" : "false")}</EnableChartPicker>");
        xml.AppendLine("                        <RelationshipName></RelationshipName>");
        xml.AppendLine("                      </parameters>");
        xml.AppendLine("                    </control>");
    }

    private static void EmitIframeControl(StringBuilder xml, string controlSlug, DashboardCellSpec cell)
    {
        xml.AppendLine($"                    <control id=\"{controlSlug}\" classid=\"{IframeClassId}\">");
        xml.AppendLine("                      <parameters>");
        xml.AppendLine($"                        <Url>{XmlEscape(cell.IframeUrl ?? string.Empty)}</Url>");
        xml.AppendLine($"                        <PassParameters>{(cell.IframePassParameters ? "true" : "false")}</PassParameters>");
        // <Security>true</Security> RESTRICTS cross-frame scripting (the safe default).
        // false allows it. Per Microsoft docs, user-owned dashboards always restrict;
        // org-owned (us) can choose.
        xml.AppendLine($"                        <Security>{(cell.IframeRestrictCrossFrameScripting ? "true" : "false")}</Security>");
        xml.AppendLine($"                        <Scrolling>{(cell.IframeScrolling ? "true" : "false")}</Scrolling>");
        xml.AppendLine($"                        <Border>{(cell.IframeBorder ? "true" : "false")}</Border>");
        xml.AppendLine("                      </parameters>");
        xml.AppendLine("                    </control>");
    }

    private static void EmitWebResourceControl(StringBuilder xml, string controlSlug, DashboardCellSpec cell)
    {
        xml.AppendLine($"                    <control id=\"{controlSlug}\" classid=\"{WebResourceClassId}\" isunbound=\"true\">");
        xml.AppendLine("                      <parameters>");
        xml.AppendLine($"                        <WebResourceId>{XmlEscape(cell.WebResourceId ?? string.Empty)}</WebResourceId>");
        if (!string.IsNullOrWhiteSpace(cell.WebResourceUrl))
            xml.AppendLine($"                        <Url>{XmlEscape(cell.WebResourceUrl)}</Url>");
        xml.AppendLine("                      </parameters>");
        xml.AppendLine("                    </control>");
    }

    private static string NewGuid() => "{" + Guid.NewGuid().ToString().ToLowerInvariant() + "}";

    private static int _slugCounter;
    private static string NewControlSlug(string type) =>
        $"{type}_{System.Threading.Interlocked.Increment(ref _slugCounter)}";

    private static string XmlEscape(string s) =>
        string.IsNullOrEmpty(s) ? string.Empty :
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
