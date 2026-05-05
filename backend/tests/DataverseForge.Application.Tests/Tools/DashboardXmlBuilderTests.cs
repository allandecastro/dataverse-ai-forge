using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tests.Tools;

public class DashboardXmlBuilderTests
{
    private static DashboardLayoutSpec OneCellSpec(DashboardCellSpec cell) =>
        new(new List<DashboardTabSpec>
        {
            new("Main", "Main", Columns: new List<DashboardColumnSpec>
            {
                new("100%", new List<DashboardSectionSpec>
                {
                    new("S", "S", Cells: new List<DashboardCellSpec> { cell })
                })
            })
        });

    [Fact]
    public void Render_ChartCell_EmitsChartGridControlWithVisualizationId()
    {
        var visId = Guid.NewGuid();
        var viewId = Guid.NewGuid();
        var spec = OneCellSpec(new DashboardCellSpec(
            CellType: "chart",
            TargetEntityType: "opportunity",
            ViewId: viewId.ToString(),
            VisualizationId: visId.ToString()));

        var xml = DashboardXmlBuilder.Render(spec);

        Assert.StartsWith("<form>", xml);
        Assert.Contains("<TargetEntityType>opportunity</TargetEntityType>", xml);
        Assert.Contains("<ChartGridMode>Chart</ChartGridMode>", xml);
        Assert.Contains($"<VisualizationId>{{{visId}}}</VisualizationId>", xml);
        Assert.Contains($"<ViewId>{{{viewId}}}</ViewId>", xml);
        // The shared classid for chart/grid cells matches Microsoft's sample.
        Assert.Contains(DashboardXmlBuilder.ChartGridClassId, xml);
    }

    [Fact]
    public void Render_GridCell_EmitsChartGridModeGrid()
    {
        var viewId = Guid.NewGuid();
        var spec = OneCellSpec(new DashboardCellSpec(
            CellType: "grid",
            TargetEntityType: "contact",
            ViewId: viewId.ToString(),
            RecordsPerPage: 10));

        var xml = DashboardXmlBuilder.Render(spec);
        Assert.Contains("<ChartGridMode>Grid</ChartGridMode>", xml);
        Assert.Contains("<RecordsPerPage>10</RecordsPerPage>", xml);
    }

    [Fact]
    public void Render_AllCell_EmitsChartGridModeAll()
    {
        var spec = OneCellSpec(new DashboardCellSpec(
            CellType: "all",
            TargetEntityType: "lead",
            ViewId: Guid.NewGuid().ToString(),
            VisualizationId: Guid.NewGuid().ToString()));

        var xml = DashboardXmlBuilder.Render(spec);
        Assert.Contains("<ChartGridMode>All</ChartGridMode>", xml);
    }

    [Fact]
    public void Render_IframeCell_EmitsIframeClassidAndUrl()
    {
        var spec = OneCellSpec(new DashboardCellSpec(
            CellType: "iframe",
            IframeUrl: "https://example.com/dashboard",
            IframeRestrictCrossFrameScripting: true));

        var xml = DashboardXmlBuilder.Render(spec);
        Assert.Contains(DashboardXmlBuilder.IframeClassId, xml);
        Assert.Contains("<Url>https://example.com/dashboard</Url>", xml);
        // Security=true RESTRICTS cross-frame scripting (the safe default).
        Assert.Contains("<Security>true</Security>", xml);
    }

    [Fact]
    public void Render_WebResourceCell_EmitsWebResourceClassidAndId()
    {
        var resourceId = Guid.NewGuid();
        var spec = OneCellSpec(new DashboardCellSpec(
            CellType: "webresource",
            WebResourceId: resourceId.ToString()));

        var xml = DashboardXmlBuilder.Render(spec);
        Assert.Contains(DashboardXmlBuilder.WebResourceClassId, xml);
        Assert.Contains($"<WebResourceId>{resourceId}</WebResourceId>", xml);
    }

    [Fact]
    public void Render_RowSpan_EmitsTrailingEmptyRows()
    {
        // rowspan=12 should produce 1 cell-row + 11 trailing <row></row> rows.
        var spec = OneCellSpec(new DashboardCellSpec(
            CellType: "chart",
            TargetEntityType: "opportunity",
            ViewId: Guid.NewGuid().ToString(),
            VisualizationId: Guid.NewGuid().ToString(),
            RowSpan: 12));

        var xml = DashboardXmlBuilder.Render(spec);
        var emptyRows = System.Text.RegularExpressions.Regex.Matches(xml, "<row></row>").Count;
        Assert.Equal(11, emptyRows);
    }

    // ─── Validation ───

    [Fact]
    public void Validate_TabWithNoSection_Rejected()
    {
        // Microsoft's docs flag this as a known dashboard-creation gotcha.
        var spec = new DashboardLayoutSpec(new List<DashboardTabSpec>
        {
            new("Empty", "Empty", Columns: new List<DashboardColumnSpec>
            {
                new("100%", new List<DashboardSectionSpec>())
            })
        });

        var errors = DashboardXmlBuilder.Validate(spec);
        Assert.Contains(errors, e => e.Contains("at least one section", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ChartCellWithoutVisualizationId_Rejected()
    {
        var spec = OneCellSpec(new DashboardCellSpec(
            CellType: "chart",
            TargetEntityType: "opportunity"));

        var errors = DashboardXmlBuilder.Validate(spec);
        Assert.Contains(errors, e => e.Contains("visualizationId is required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ChartCellWithoutViewId_Rejected()
    {
        // Charts source their rows from a view's FetchXML — without a ViewId
        // the chart cell renders empty (real session 2026-05-05). Validation
        // must require ViewId for chart cells too, not just grid/all.
        var spec = OneCellSpec(new DashboardCellSpec(
            CellType: "chart",
            TargetEntityType: "opportunity",
            VisualizationId: Guid.NewGuid().ToString()));

        var errors = DashboardXmlBuilder.Validate(spec);
        Assert.Contains(errors, e => e.Contains("viewId is required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_GridCellWithAutoExpandAuto_Rejected()
    {
        var spec = OneCellSpec(new DashboardCellSpec(
            CellType: "grid",
            TargetEntityType: "contact",
            ViewId: Guid.NewGuid().ToString(),
            AutoExpand: "Auto"));

        var errors = DashboardXmlBuilder.Validate(spec);
        Assert.Contains(errors, e => e.Contains("autoExpand", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_IframeCellWithoutUrl_Rejected()
    {
        var spec = OneCellSpec(new DashboardCellSpec(CellType: "iframe"));
        var errors = DashboardXmlBuilder.Validate(spec);
        Assert.Contains(errors, e => e.Contains("iframeUrl is required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WebResourceWithEmptyGuid_Rejected()
    {
        var spec = OneCellSpec(new DashboardCellSpec(
            CellType: "webresource",
            WebResourceId: "00000000-0000-0000-0000-000000000000"));

        var errors = DashboardXmlBuilder.Validate(spec);
        Assert.Contains(errors, e => e.Contains("all-zero GUID", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_TooManyControls_Rejected()
    {
        // Default cap is 6.
        var cells = Enumerable.Range(0, 8).Select(_ => new DashboardCellSpec(
            CellType: "chart",
            TargetEntityType: "opportunity",
            ViewId: Guid.NewGuid().ToString(),
            VisualizationId: Guid.NewGuid().ToString())).ToList();

        var spec = new DashboardLayoutSpec(new List<DashboardTabSpec>
        {
            new("Main", "Main", Columns: new List<DashboardColumnSpec>
            {
                new("100%", new List<DashboardSectionSpec>
                {
                    new("S", "S", Cells: cells)
                })
            })
        });

        var errors = DashboardXmlBuilder.Validate(spec, maxControls: 6);
        Assert.Contains(errors, e => e.Contains("controls but the platform default cap is 6", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_UnknownCellType_Rejected()
    {
        var spec = OneCellSpec(new DashboardCellSpec(CellType: "hologram"));
        var errors = DashboardXmlBuilder.Validate(spec);
        Assert.Contains(errors, e => e.Contains("cellType 'hologram' is invalid", StringComparison.Ordinal));
    }
}
