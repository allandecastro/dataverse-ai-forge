using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

/// <summary>
/// Templates ship verbatim from Microsoft's sample-charts article. Tests guard
/// against accidentally breaking the placeholder substitution or shipping
/// unsubstituted markers.
/// </summary>
public class ChartTemplatesTests
{
    [Theory]
    [InlineData("column")]
    [InlineData("bar")]
    [InlineData("line")]
    [InlineData("pie")]
    [InlineData("funnel")]
    [InlineData("doughnut")]
    [InlineData("area")]
    [InlineData("multiseries")]
    [InlineData("comparison")]
    [InlineData("comparison100")]
    public void Render_AllSupportedTypes_ReturnsValidXml(string chartType)
    {
        var xml = ChartTemplates.Render(
            chartType,
            chartTitle: "My Chart",
            xAxisTitle: "Category",
            yAxisTitle: "Value",
            seriesLabel1: "S1",
            seriesLabel2: "S2",
            paletteCustomColors: null);

        Assert.NotEmpty(xml);
        Assert.StartsWith("<Chart", xml);
        Assert.EndsWith("</Chart>", xml.TrimEnd('\r', '\n'));
        // No leftover unsubstituted markers — every {FOO} placeholder must be
        // replaced before we ship the XML to Dataverse.
        Assert.DoesNotContain("{TITLE_ATTRS}", xml);
        Assert.DoesNotContain("{X_AXIS_ATTRS}", xml);
        Assert.DoesNotContain("{Y_AXIS_ATTRS}", xml);
        Assert.DoesNotContain("{PALETTE_ATTRS}", xml);
        Assert.DoesNotContain("{SERIES_LABEL_1}", xml);
        Assert.DoesNotContain("{SERIES_LABEL_2}", xml);
    }

    [Fact]
    public void Render_WithChartTitle_EmitsTitleText()
    {
        var xml = ChartTemplates.Render("column", "Top 10 Customers", "", "", null, null, null);
        Assert.Contains("Text=\"Top 10 Customers\"", xml);
    }

    [Fact]
    public void Render_WithoutChartTitle_DoesNotEmitTextAttr()
    {
        var xml = ChartTemplates.Render("column", null, "", "", null, null, null);
        Assert.DoesNotContain("Text=", xml);
    }

    [Fact]
    public void Render_WithCustomPalette_AddsCustomColorOverride()
    {
        var xml = ChartTemplates.Render("column", null, "", "", null, null, "91,151,213; 237,125,49");
        // Modern UCI overrides custom colors unless CustomColorOverride=true is set —
        // the templater must include it whenever a palette is provided.
        Assert.Contains("PaletteCustomColors=\"91,151,213; 237,125,49\"", xml);
        Assert.Contains("CustomColorOverride=\"true\"", xml);
    }

    [Fact]
    public void Render_MultiSeries_EmitsBothSeriesLabels()
    {
        var xml = ChartTemplates.Render(
            "multiseries", null, "", "",
            "Estimated", "Actual", null);
        Assert.Contains("LegendText=\"Estimated\"", xml);
        Assert.Contains("LegendText=\"Actual\"", xml);
    }

    [Fact]
    public void Render_AxisTitles_AddTitleAttrToBothAxes()
    {
        var xml = ChartTemplates.Render(
            "column", null, "Industry", "Revenue", null, null, null);
        // X title lives on AxisX, Y title on AxisY — neither should leak to the other.
        Assert.Contains("<AxisX Title=\"Industry\"", xml);
        Assert.Contains("<AxisY Title=\"Revenue\"", xml);
    }

    [Fact]
    public void Render_UnsupportedType_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ChartTemplates.Render("3dholograph", null, "", "", null, null, null));
    }

    [Theory]
    [InlineData("column", false)]
    [InlineData("multiseries", true)]
    [InlineData("comparison", false)]
    public void IsMultiSeries_OnlyMultiseriesType(string ct, bool expected)
        => Assert.Equal(expected, ChartTemplates.IsMultiSeries(ct));

    [Theory]
    [InlineData("column", false)]
    [InlineData("comparison", true)]
    [InlineData("comparison100", true)]
    [InlineData("pie", false)]
    public void IsComparison_OnlyComparisonTypes(string ct, bool expected)
        => Assert.Equal(expected, ChartTemplates.IsComparison(ct));

    [Fact]
    public void IsSupported_RecognizesAllSupportedTypes()
    {
        foreach (var t in ChartTemplates.SupportedChartTypes)
            Assert.True(ChartTemplates.IsSupported(t), $"Expected '{t}' to be supported.");
    }

    [Fact]
    public void IsSupported_RejectsUnknownType()
    {
        Assert.False(ChartTemplates.IsSupported("hologram"));
        Assert.False(ChartTemplates.IsSupported(""));
    }

    // ─── Phase B: axis & value LabelFormat substitution ───

    [Fact]
    public void Render_XAxisLabelFormat_AddsFormatAttrToAxisXLabelStyle()
    {
        var xml = ChartTemplates.Render(
            "column", null, "", "", null, null, null,
            xAxisLabelFormat: "MMM yy");

        // The AxisX block contains a LabelStyle whose Format attr should now
        // be set. The AxisY one should NOT carry it.
        var axisXIdx = xml.IndexOf("<AxisX", StringComparison.Ordinal);
        var axisYIdx = xml.IndexOf("<AxisY", StringComparison.Ordinal);
        Assert.True(axisXIdx > 0 && axisYIdx > 0);

        // Find LabelStyle within AxisX block
        var axisXEndIdx = xml.IndexOf("</AxisX>", axisXIdx, StringComparison.Ordinal);
        var axisXBlock = xml.Substring(axisXIdx, axisXEndIdx - axisXIdx);
        Assert.Contains("Format=\"MMM yy\"", axisXBlock);

        // AxisY block should NOT have the format
        var axisYEndIdx = xml.IndexOf("</AxisY>", axisYIdx, StringComparison.Ordinal);
        var axisYBlock = xml.Substring(axisYIdx, axisYEndIdx - axisYIdx);
        Assert.DoesNotContain("Format=", axisYBlock);
    }

    [Fact]
    public void Render_YAxisLabelFormat_AddsFormatAttrToAxisYOnly()
    {
        var xml = ChartTemplates.Render(
            "column", null, "", "", null, null, null,
            yAxisLabelFormat: "C0");

        var axisYIdx = xml.IndexOf("<AxisY", StringComparison.Ordinal);
        var axisYEndIdx = xml.IndexOf("</AxisY>", axisYIdx, StringComparison.Ordinal);
        var axisYBlock = xml.Substring(axisYIdx, axisYEndIdx - axisYIdx);
        Assert.Contains("Format=\"C0\"", axisYBlock);
    }

    [Fact]
    public void Render_ValueLabelFormat_AddsLabelFormatToSeries()
    {
        var xml = ChartTemplates.Render(
            "column", null, "", "", null, null, null,
            valueLabelFormat: "P0");

        Assert.Contains("LabelFormat=\"P0\"", xml);
    }

    [Fact]
    public void Render_PieValueLabelFormat_AppearsOnSeries()
    {
        // Pie charts always show value labels — verify the format flows through
        // even though pie has a different Series shape than column.
        var xml = ChartTemplates.Render(
            "pie", null, "", "", null, null, null,
            valueLabelFormat: "P1");

        Assert.Contains("LabelFormat=\"P1\"", xml);
    }

    [Fact]
    public void Render_LineChartAxisLabelFormat_FlowsThroughDifferentTemplate()
    {
        // Line uses different MajorGrid color — ensure the placeholder still
        // got installed in that template.
        var xml = ChartTemplates.Render(
            "line", null, "", "", null, null, null,
            xAxisLabelFormat: "yyyy-MM");

        Assert.Contains("Format=\"yyyy-MM\"", xml);
    }

    [Fact]
    public void Render_NoLabelFormats_NoFormatAttrEmitted()
    {
        // Default behaviour — no format params, no Format attribute anywhere.
        var xml = ChartTemplates.Render(
            "column", null, "", "", null, null, null);

        Assert.DoesNotContain(" Format=\"", xml);
        Assert.DoesNotContain(" LabelFormat=\"", xml);
    }

    [Fact]
    public void Render_AllFormatPlaceholdersResolved_NoLeftoverMarkers()
    {
        // Every placeholder we added in Phase B must be substituted on every
        // chart type, even when the agent passes empty strings.
        foreach (var ct in ChartTemplates.SupportedChartTypes)
        {
            var xml = ChartTemplates.Render(
                ct, null, "", "", null, null, null,
                xAxisLabelFormat: "X", yAxisLabelFormat: "Y", valueLabelFormat: "V");
            Assert.DoesNotContain("{X_AXIS_LABEL_FMT}", xml);
            Assert.DoesNotContain("{Y_AXIS_LABEL_FMT}", xml);
            Assert.DoesNotContain("{VALUE_LABEL_FMT}", xml);
        }
    }

    // ─── Color rules: emission disabled — Dataverse rejected the
    // <Color><Conditional><ColorRule>...</ColorRule></Conditional></Color>
    // shape in production (HTTP 400 0x8004e016, real session 2026-05-05).
    // We still validate the JSON so the plan tool can surface precise
    // rejections, but Render no longer injects anything.

    [Fact]
    public void Render_ColorRulesJson_EmitsNoConditionalBlock()
    {
        // Even with valid rules, Render must NOT emit <Conditional>/<ColorRule>
        // — the underlying Dataverse schema doesn't accept them and the chart
        // create would fail at HTTP 400. The chart still renders with the
        // default palette (or paletteCustomColors if passed).
        var rules = """
            [
              {"when":"1","color":"#D62728"},
              {"when":"2","color":"#FFC000"},
              {"when":"3","color":"#2CA02C"}
            ]
            """;
        var xml = ChartTemplates.Render(
            "pie", null, "", "", null, null, null,
            colorRulesJson: rules);

        Assert.DoesNotContain("<Conditional>", xml);
        Assert.DoesNotContain("<ColorRule>", xml);
        // Placeholder must be substituted (not left as a raw token).
        Assert.DoesNotContain("{COLOR_RULES}", xml);
    }

    [Fact]
    public void Render_NoColorRules_NoColorBlockEmitted()
    {
        var xml = ChartTemplates.Render(
            "pie", null, "", "", null, null, null);

        Assert.DoesNotContain("<Conditional>", xml);
        Assert.DoesNotContain("<ColorRule>", xml);
    }

    [Fact]
    public void ValidateAndRenderColorRules_RejectsInvalidJson()
    {
        // Validation still surfaces precise errors so PlanCreateChartTool can
        // reject malformed payloads instead of silently dropping them.
        var ex = Assert.Throws<ArgumentException>(() =>
            ChartTemplates.ValidateAndRenderColorRules("not json"));
        Assert.Contains("not valid JSON", ex.Message);
    }

    [Fact]
    public void ValidateAndRenderColorRules_RejectsEmptyArray()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            ChartTemplates.ValidateAndRenderColorRules("[]"));
        Assert.Contains("non-empty array", ex.Message);
    }

    [Fact]
    public void ValidateAndRenderColorRules_RejectsRuleMissingFields()
    {
        Assert.Throws<ArgumentException>(() =>
            ChartTemplates.ValidateAndRenderColorRules("[{\"when\":\"1\"}]"));
        Assert.Throws<ArgumentException>(() =>
            ChartTemplates.ValidateAndRenderColorRules("[{\"color\":\"#FF0000\"}]"));
    }

    [Fact]
    public void ValidateAndRenderColorRules_ValidPayload_ReturnsEmptyString()
    {
        // Even a valid payload yields empty XML — the feature is no-op so the
        // rest of the chart pipeline keeps working without injecting unsupported
        // markup. Plan tool surfaces ColorRulesNotSupportedReason as a warning.
        var rules = """[{"when":"1","color":"#D62728"}]""";
        var rendered = ChartTemplates.ValidateAndRenderColorRules(rules);
        Assert.Equal(string.Empty, rendered);
    }
}
