using System.Text.Json;

namespace DataverseForge.Application.Tools;

/// <summary>
/// PresentationDescription templates per chart type. Each one is the verbatim
/// shape Dataverse emits for its built-in charts (sourced from Microsoft's
/// public sample-charts article — same structure that the chart designer in
/// the maker portal generates).
///
/// We don't try to generate these from scratch: the surface is too large and
/// Microsoft doesn't publish a schema. Instead we ship one template per
/// ChartType, and the planner lightly substitutes a small set of placeholders
/// (chart title, axis titles, series legend text, optional palette).
///
/// Placeholders we substitute (all optional):
///   {TITLE_TEXT}         — chart title shown above the plot area
///   {X_AXIS_TITLE}       — X-axis label
///   {Y_AXIS_TITLE}       — Y-axis label (single-series)
///   {SERIES_LABEL_1}     — legend entry for measure 1 (multi-series only)
///   {SERIES_LABEL_2}     — legend entry for measure 2 (multi-series only)
///   {PALETTE_OVERRIDE}   — when an agent provides PaletteCustomColors, we emit
///                          the corresponding attributes on the &lt;Chart&gt; element;
///                          otherwise we fall through to the default palette.
///   {X_AXIS_LABEL_FMT}   — optional Format attribute on the X-axis LabelStyle
///                          (e.g. <c>"MMM yy"</c> for "Apr 2026" date axes,
///                          <c>"C0"</c> for currency, <c>"P0"</c> for percentages)
///   {Y_AXIS_LABEL_FMT}   — same for the Y-axis
///   {VALUE_LABEL_FMT}    — optional LabelFormat attribute on the Series element
///                          (formats the data labels themselves; only takes
///                          effect on chart types that ship with
///                          IsValueShownAsLabel="True" — i.e. column, line,
///                          pie, funnel, doughnut)
/// </summary>
public static class ChartTemplates
{
    /// <summary>The chart-type values <c>plan_create_chart</c> accepts. Mapped
    /// 1:1 to a template below; rejects everything else.</summary>
    public static readonly string[] SupportedChartTypes =
    [
        "column",                 // single-series Column
        "bar",                    // single-series Bar
        "line",                   // single-series Line
        "pie",                    // single-series Pie
        "funnel",                 // single-series Funnel
        "doughnut",               // single-series Doughnut
        "area",                   // single-series Area (template is StackedColumn under the hood,
                                  //                    matching Microsoft's sample for "area")
        "multiseries",            // two measures, single category
        "comparison",             // StackedColumn with two groupby (category + sub-group)
        "comparison100",          // StackedBar100 with two groupby
    ];

    public static bool IsSupported(string chartType) =>
        Array.IndexOf(SupportedChartTypes, (chartType ?? "").ToLowerInvariant()) >= 0;

    /// <summary>
    /// Returns whether the named type is a multi-measure chart (two series).
    /// Drives DataDescription emission — multi-measure charts have two
    /// <c>&lt;measurecollection&gt;</c> elements.
    /// </summary>
    public static bool IsMultiSeries(string chartType) =>
        string.Equals(chartType, "multiseries", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns whether the chart uses two groupby clauses (category + sub-group).
    /// Comparison stacked / 100% stacked charts use this shape.
    /// </summary>
    public static bool IsComparison(string chartType)
    {
        var lower = (chartType ?? "").ToLowerInvariant();
        return lower == "comparison" || lower == "comparison100";
    }

    /// <summary>
    /// Returns whether single-measure charts of this type render a legend by
    /// default (pie/funnel/doughnut do; column/bar/line don't). Used to seed
    /// the legend default when the agent doesn't override.
    /// </summary>
    public static bool DefaultLegendEnabled(string chartType)
    {
        var lower = (chartType ?? "").ToLowerInvariant();
        return lower == "pie" || lower == "funnel" || lower == "doughnut";
    }

    /// <summary>
    /// Render the PresentationDescription XML for the requested chart type.
    /// Every placeholder is replaced with either the agent-provided value or
    /// an empty string, so we never ship raw "{TITLE_TEXT}" markers.
    /// </summary>
    public static string Render(
        string chartType,
        string? chartTitle,
        string? xAxisTitle,
        string? yAxisTitle,
        string? seriesLabel1,
        string? seriesLabel2,
        string? paletteCustomColors,
        string? xAxisLabelFormat = null,
        string? yAxisLabelFormat = null,
        string? valueLabelFormat = null,
        string? colorRulesJson = null)
    {
        var template = GetTemplate(chartType);
        var titleAttrs = string.IsNullOrWhiteSpace(chartTitle)
            ? ""
            : $" Text=\"{XmlEscape(chartTitle)}\"";
        var paletteAttrs = string.IsNullOrWhiteSpace(paletteCustomColors)
            ? ""
            // CustomColorOverride=true is required to keep the user's palette
            // in the modern UCI; otherwise the platform palette wins.
            : $" Palette=\"None\" PaletteCustomColors=\"{XmlEscape(paletteCustomColors)}\" CustomColorOverride=\"true\"";
        var xAxisAttrs = string.IsNullOrWhiteSpace(xAxisTitle)
            ? ""
            : $" Title=\"{XmlEscape(xAxisTitle)}\"";
        var yAxisAttrs = string.IsNullOrWhiteSpace(yAxisTitle)
            ? ""
            : $" Title=\"{XmlEscape(yAxisTitle)}\"";
        // Axis label formats sit on the LabelStyle inside AxisX / AxisY. We
        // emit the leading space + Format attribute or nothing so the original
        // template's whitespace is preserved.
        var xAxisLabelFmt = string.IsNullOrWhiteSpace(xAxisLabelFormat)
            ? ""
            : $" Format=\"{XmlEscape(xAxisLabelFormat)}\"";
        var yAxisLabelFmt = string.IsNullOrWhiteSpace(yAxisLabelFormat)
            ? ""
            : $" Format=\"{XmlEscape(yAxisLabelFormat)}\"";
        // Value label format → LabelFormat="..." on the Series element. Only
        // takes effect on charts where IsValueShownAsLabel is true. Templates
        // without that attribute (area, multiseries, comparison*) silently
        // ignore the format — the prompt warns the agent about that.
        var valueLabelFmt = string.IsNullOrWhiteSpace(valueLabelFormat)
            ? ""
            : $" LabelFormat=\"{XmlEscape(valueLabelFormat)}\"";

        // Conditional color rules — turns the agent's [{when, color}] JSON
        // into the <Color><Conditional><ColorRule>…</ColorRule></Conditional></Color>
        // block Dataverse expects inside <Series>. Empty or unparseable JSON
        // yields an empty string so the rest of the template renders normally.
        var colorRulesXml = BuildColorRulesXml(colorRulesJson);

        // Several templates ship with a hardcoded default palette baked into
        // the <Chart> open tag (e.g. comparison, comparison100, multiseries):
        //     <Chart Palette="None" PaletteCustomColors="…"{PALETTE_ATTRS}>
        // When the agent supplies its own paletteCustomColors, paletteAttrs
        // also expands to a `Palette=` + `PaletteCustomColors=` pair, so the
        // open tag ends up with both — Dataverse rejects with HTTP 400
        // 0x8004e016 "'Palette' is a duplicate attribute name". Real session
        // 2026-05-05 hit this on a comparison100 chart create.
        // Strip the hardcoded pair from the first <Chart open tag before the
        // {PALETTE_ATTRS} substitution so the agent's override stands alone.
        // No-op when the agent didn't pass a palette — the hardcoded default
        // remains intact.
        if (!string.IsNullOrWhiteSpace(paletteCustomColors))
        {
            template = StripHardcodedPaletteFromChartOpen(template);
        }

        return template
            .Replace("{TITLE_ATTRS}", titleAttrs)
            .Replace("{PALETTE_ATTRS}", paletteAttrs)
            .Replace("{X_AXIS_ATTRS}", xAxisAttrs)
            .Replace("{Y_AXIS_ATTRS}", yAxisAttrs)
            .Replace("{X_AXIS_LABEL_FMT}", xAxisLabelFmt)
            .Replace("{Y_AXIS_LABEL_FMT}", yAxisLabelFmt)
            .Replace("{VALUE_LABEL_FMT}", valueLabelFmt)
            .Replace("{COLOR_RULES}", colorRulesXml)
            .Replace("{SERIES_LABEL_1}", XmlEscape(seriesLabel1 ?? "Series 1"))
            .Replace("{SERIES_LABEL_2}", XmlEscape(seriesLabel2 ?? "Series 2"));
    }

    /// <summary>
    /// Validates the shape of a colorRulesJson payload BUT does not emit any
    /// XML — Dataverse's chart `&lt;Color&gt;` element is a scalar
    /// `System.Drawing.Color` value, not a container, and any nested
    /// `&lt;Conditional&gt;`/`&lt;ColorRule&gt;` block we tried to inject got
    /// rejected at chart-create with HTTP 400 0x8004e016 (real session traces
    /// from 2026-05-05). Returning an empty string keeps the rest of the
    /// chart pipeline working; the plan tool surfaces a "not supported"
    /// warning so the agent can offer the user a form-script alternative.
    /// </summary>
    /// <remarks>
    /// We still validate the JSON so the plan tool can surface a precise
    /// rejection if the agent passed a malformed payload — that's still a
    /// caller bug worth flagging. <see cref="ColorRulesNotSupportedReason"/>
    /// is the canonical message to forward to the user.
    /// </remarks>
    public static string ValidateAndRenderColorRules(string colorRulesJson)
    {
        if (string.IsNullOrWhiteSpace(colorRulesJson))
            throw new ArgumentException("colorRulesJson is empty.", nameof(colorRulesJson));

        List<ColorRuleSpec>? rules;
        try
        {
            rules = JsonSerializer.Deserialize<List<ColorRuleSpec>>(colorRulesJson, _colorRulesJsonOptions);
        }
        catch (JsonException jx)
        {
            throw new ArgumentException(
                $"colorRulesJson is not valid JSON: {jx.Message}", nameof(colorRulesJson), jx);
        }

        if (rules is null || rules.Count == 0)
            throw new ArgumentException("colorRulesJson must be a non-empty array.", nameof(colorRulesJson));

        for (var i = 0; i < rules.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(rules[i].When))
                throw new ArgumentException($"colorRulesJson[{i}].when is required.", nameof(colorRulesJson));
            if (string.IsNullOrWhiteSpace(rules[i].Color))
                throw new ArgumentException($"colorRulesJson[{i}].color is required.", nameof(colorRulesJson));
        }
        // Intentionally no-op — see remarks. Returning empty so the chart
        // template still renders (just without the unsupported Conditional block).
        return string.Empty;
    }

    /// <summary>
    /// User-facing message explaining why <c>colorRulesJson</c> /
    /// <c>colorByOptionSet</c> can't be honored. Surfaced by
    /// <see cref="PlanCreateChartTool"/> as a warning when either parameter is
    /// passed so the agent can pivot to suggesting a form-script approach.
    /// </summary>
    public const string ColorRulesNotSupportedReason =
        "Per-value chart colors (colorRulesJson / colorByOptionSet) aren't supported by Dataverse "
        + "server-side charts: the chart XML's <Color> element is a scalar System.Drawing.Color, "
        + "not a container, so any <Conditional>/<ColorRule> we emit gets rejected at create. The "
        + "chart will be created with the default palette (or paletteCustomColors if you passed one). "
        + "Conditional coloring per option set is typically done in the maker portal via a form "
        + "script that recolors the rendered chart points.";

    /// <summary>
    /// Internal entry point used by <see cref="Render"/>. Always returns empty
    /// — see <see cref="ValidateAndRenderColorRules"/> for the rationale.
    /// Kept as a dedicated method so the call site in <see cref="Render"/>
    /// remains stable if we revisit this later (e.g. via a form-script
    /// emission path).
    /// </summary>
    private static string BuildColorRulesXml(string? colorRulesJson)
    {
        _ = colorRulesJson; // intentionally ignored — feature is no-op.
        return string.Empty;
    }

    private static readonly JsonSerializerOptions _colorRulesJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Validation-only — we still parse the agent's payload to give precise error
    // messages on malformed JSON, even though the rendered output is empty.
    private sealed record ColorRuleSpec(string? When, string? Color, string? Operator);

    private static string GetTemplate(string chartType) => (chartType ?? "").ToLowerInvariant() switch
    {
        "column" => Column,
        "bar" => Bar,
        "line" => Line,
        "pie" => Pie,
        "funnel" => Funnel,
        "doughnut" => Doughnut,
        "area" => Area,
        "multiseries" => MultiSeries,
        "comparison" => ComparisonStacked,
        "comparison100" => Comparison100Stacked,
        _ => throw new ArgumentException($"Unsupported chart type '{chartType}'. Supported: {string.Join(", ", SupportedChartTypes)}", nameof(chartType))
    };

    private static string XmlEscape(string s)
        => string.IsNullOrEmpty(s) ? string.Empty :
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>
    /// Removes a hardcoded `Palette="None" PaletteCustomColors="…"` pair from
    /// the FIRST `<Chart …>` open tag of <paramref name="template"/>. Used
    /// when the agent supplies its own palette so the substitution doesn't
    /// produce a duplicate-attribute XML that Dataverse rejects.
    ///
    /// Only touches the first open tag (templates only have one `<Chart>`)
    /// and only matches the well-known shape baked into our templates — a
    /// stricter regex would be brittle, a broader one risks scrubbing legit
    /// content elsewhere.
    /// </summary>
    private static string StripHardcodedPaletteFromChartOpen(string template)
    {
        // Pattern: ` Palette="None" PaletteCustomColors="...non-quote chars..."`
        // anchored to the first `<Chart ` open. Note the leading space — we
        // want to swallow the separator so the result reads cleanly.
        var pattern = new System.Text.RegularExpressions.Regex(
            "(?<=<Chart)\\s+Palette=\"None\"\\s+PaletteCustomColors=\"[^\"]*\"");
        return pattern.Replace(template, string.Empty, count: 1);
    }

    // ───────────────────────────────────────────────────────────────────
    // Templates — verbatim from Microsoft's sample-charts article
    // (https://learn.microsoft.com/en-us/power-apps/developer/data-platform/sample-charts).
    // We adjusted only:
    //   1. Placeholders for title / axis titles (so the agent can name them)
    //   2. {PALETTE_ATTRS} so a custom palette flows through to the <Chart> root
    //   3. Multi-series uses {SERIES_LABEL_1}/{SERIES_LABEL_2} so the agent can
    //      name the two measures (e.g. "Estimated" vs "Actual")
    // ───────────────────────────────────────────────────────────────────

    private const string Column = """
<Chart{PALETTE_ATTRS}>
  <Series>
    <Series ChartType="Column" IsValueShownAsLabel="True" Color="91, 151, 213" BackSecondaryColor="112, 142, 50" Font="{0}, 9.5px" LabelForeColor="59, 59, 59" CustomProperties="PointWidth=0.75, MaxPixelPointWidth=40"{VALUE_LABEL_FMT}>
      <SmartLabelStyle Enabled="True" />{COLOR_RULES}
    </Series>
  </Series>
  <ChartAreas>
    <ChartArea BorderColor="White" BorderDashStyle="Solid">
      <AxisY{Y_AXIS_ATTRS} LabelAutoFitMinFontSize="8" TitleForeColor="59, 59, 59" TitleFont="{0}, 10.5px" LineColor="165, 172, 181" IsReversed="False">
        <MajorGrid LineColor="239, 242, 246" />
        <LabelStyle Font="{0}, 10.5px" ForeColor="59, 59, 59"{Y_AXIS_LABEL_FMT} />
      </AxisY>
      <AxisX{X_AXIS_ATTRS} LabelAutoFitMinFontSize="8" TitleForeColor="59, 59, 59" TitleFont="{0}, 10.5px" LineColor="165, 172, 181" IsReversed="False">
        <MajorGrid Enabled="False" />
        <MajorTickMark Enabled="False" />
        <LabelStyle Font="{0}, 10.5px" ForeColor="59, 59, 59"{X_AXIS_LABEL_FMT} />
      </AxisX>
    </ChartArea>
  </ChartAreas>
  <Titles>
    <Title{TITLE_ATTRS} DockingOffset="-3" Font="{0}, 13px" ForeColor="59, 59, 59" Alignment="TopLeft" />
  </Titles>
</Chart>
""";

    private const string Bar = """
<Chart{PALETTE_ATTRS}>
  <Series>
    <Series ChartType="Bar" IsValueShownAsLabel="False" Color="91, 151, 213" BackSecondaryColor="112, 142, 50" Font="{0}, 9.5px" LabelForeColor="59, 59, 59" CustomProperties="PointWidth=0.75, MaxPixelPointWidth=40"{VALUE_LABEL_FMT}>
      <SmartLabelStyle Enabled="True" />{COLOR_RULES}
    </Series>
  </Series>
  <ChartAreas>
    <ChartArea BorderColor="White" BorderDashStyle="Solid">
      <AxisY{Y_AXIS_ATTRS} LabelAutoFitMinFontSize="8" TitleForeColor="59, 59, 59" TitleFont="{0}, 10.5px" LineColor="165, 172, 181" IsReversed="False">
        <MajorGrid LineColor="239, 242, 246" />
        <LabelStyle Font="{0}, 10.5px" ForeColor="59, 59, 59"{Y_AXIS_LABEL_FMT} />
      </AxisY>
      <AxisX{X_AXIS_ATTRS} LabelAutoFitMinFontSize="8" TitleForeColor="59, 59, 59" TitleFont="{0}, 10.5px" LineColor="165, 172, 181" IsReversed="False">
        <MajorGrid Enabled="False" />
        <MajorTickMark Enabled="False" />
        <LabelStyle Font="{0}, 10.5px" ForeColor="59, 59, 59"{X_AXIS_LABEL_FMT} />
      </AxisX>
    </ChartArea>
  </ChartAreas>
  <Titles>
    <Title{TITLE_ATTRS} DockingOffset="-3" Font="{0}, 13px" ForeColor="59, 59, 59" Alignment="TopLeft" />
  </Titles>
</Chart>
""";

    private const string Line = """
<Chart{PALETTE_ATTRS}>
  <Series>
    <Series IsValueShownAsLabel="True" BorderWidth="3" ChartType="Line" Color="49, 171, 204" MarkerStyle="Square" MarkerSize="9" MarkerColor="37, 128, 153"{VALUE_LABEL_FMT} />
  </Series>
  <ChartAreas>
    <ChartArea BorderColor="White">
      <AxisY{Y_AXIS_ATTRS} LabelAutoFitMinFontSize="8" TitleForeColor="59, 59, 59" TitleFont="{0}, 10.5px" LineColor="165, 172, 181">
        <MajorGrid LineColor="239, 242, 230" />
        <MajorTickMark LineColor="165, 172, 181" />
        <LabelStyle Font="{0}, 10.5px" ForeColor="59, 59, 59"{Y_AXIS_LABEL_FMT} />
      </AxisY>
      <AxisX{X_AXIS_ATTRS} LabelAutoFitMinFontSize="8" TitleForeColor="59, 59, 59" TitleFont="{0}, 10.5px" LineColor="165, 172, 181">
        <MajorGrid Enabled="False" />
        <LabelStyle Font="{0}, 10.5px" ForeColor="59, 59, 59"{X_AXIS_LABEL_FMT} />
      </AxisX>
    </ChartArea>
  </ChartAreas>
  <Titles>
    <Title{TITLE_ATTRS} DockingOffset="-3" Font="{0}, 13px" ForeColor="59, 59, 59" Alignment="TopLeft" />
  </Titles>
</Chart>
""";

    // Pie + Funnel + Doughnut share the structural shape but vary on ChartType
    // and CustomProperties; we keep three templates rather than a single
    // parameterised one so future tweaks per chart type don't conflict.

    private const string Pie = """
<Chart Palette="None" PaletteCustomColors="91,151,213; 237,125,49; 160,116,166; 255,192,0; 68,114,196; 112,173,71; 37,94,145; 158,72,14; 117,55,125; 153,115,0; 38,68,120; 67,104,43; 124,175,221; 241,151,90; 186,144,192; 255,205,51; 105,142,208; 140,193,104; 50,125,194; 210,96,18; 150,83,159; 204,154,0; 51,90,161; 90,138,57;"{PALETTE_ATTRS}>
  <Series>
    <Series ShadowOffset="0" IsValueShownAsLabel="true" Font="{0}, 9.5px" LabelForeColor="59, 59, 59" CustomProperties="PieLabelStyle=Inside, PieDrawingStyle=Default" ChartType="pie"{VALUE_LABEL_FMT}>
      <SmartLabelStyle Enabled="True" />{COLOR_RULES}
    </Series>
  </Series>
  <ChartAreas>
    <ChartArea>
      <Area3DStyle Enable3D="false" />
    </ChartArea>
  </ChartAreas>
  <Legends>
    <Legend Alignment="Center" LegendStyle="Table" Docking="right" Font="{0}, 11px" ShadowColor="0, 0, 0, 0" ForeColor="59, 59, 59" />
  </Legends>
  <Titles>
    <Title{TITLE_ATTRS} Alignment="TopLeft" DockingOffset="-3" Font="{0}, 13px" ForeColor="0, 0, 0" />
  </Titles>
</Chart>
""";

    private const string Funnel = """
<Chart Palette="None" PaletteCustomColors="91,151,213; 237,125,49; 160,116,166; 255,192,0; 68,114,196; 112,173,71; 37,94,145; 158,72,14; 117,55,125; 153,115,0; 38,68,120; 67,104,43; 124,175,221; 241,151,90; 186,144,192; 255,205,51; 105,142,208; 140,193,104; 50,125,194; 210,96,18; 150,83,159; 204,154,0; 51,90,161; 90,138,57;"{PALETTE_ATTRS}>
  <Series>
    <Series ShadowOffset="0" IsValueShownAsLabel="true" Font="{0}, 9.5px" LabelForeColor="59, 59, 59" ChartType="Funnel" CustomProperties="FunnelLabelStyle=Outside, FunnelNeckHeight=0, FunnelPointGap=1, FunnelNeckWidth=5"{VALUE_LABEL_FMT}>
      <SmartLabelStyle Enabled="True" />{COLOR_RULES}
    </Series>
  </Series>
  <ChartAreas>
    <ChartArea>
      <Area3DStyle Enable3D="false" />
    </ChartArea>
  </ChartAreas>
  <Legends>
    <Legend Alignment="Center" LegendStyle="Table" Docking="right" Font="{0}, 11px" ShadowColor="0, 0, 0, 0" ForeColor="59, 59, 59" />
  </Legends>
  <Titles>
    <Title{TITLE_ATTRS} Alignment="TopLeft" DockingOffset="-3" Font="Segeo UI, 13px" ForeColor="0, 0, 0" />
  </Titles>
</Chart>
""";

    private const string Doughnut = """
<Chart Palette="None" PaletteCustomColors="91,151,213; 237,125,49; 160,116,166; 255,192,0; 68,114,196; 112,173,71; 37,94,145; 158,72,14; 117,55,125; 153,115,0; 38,68,120; 67,104,43;"{PALETTE_ATTRS}>
  <Series>
    <Series ShadowOffset="0" IsValueShownAsLabel="true" Font="{0}, 9.5px" LabelForeColor="59, 59, 59" CustomProperties="PieLabelStyle=Inside, PieDrawingStyle=Default, DoughnutRadius=60" ChartType="Doughnut"{VALUE_LABEL_FMT}>
      <SmartLabelStyle Enabled="True" />{COLOR_RULES}
    </Series>
  </Series>
  <ChartAreas>
    <ChartArea>
      <Area3DStyle Enable3D="false" />
    </ChartArea>
  </ChartAreas>
  <Legends>
    <Legend Alignment="Center" LegendStyle="Table" Docking="right" Font="{0}, 11px" ShadowColor="0, 0, 0, 0" ForeColor="59, 59, 59" />
  </Legends>
  <Titles>
    <Title{TITLE_ATTRS} Alignment="TopLeft" DockingOffset="-3" Font="{0}, 13px" ForeColor="0, 0, 0" />
  </Titles>
</Chart>
""";

    // Microsoft's sample for "area" actually uses StackedColumn — kept verbatim.
    private const string Area = """
<Chart Palette="None" PaletteCustomColors="91,151,213; 237,125,49; 160,116,166; 255,192,0; 68,114,196; 112,173,71; 37,94,145; 158,72,14; 117,55,125; 153,115,0; 38,68,120; 67,104,43; 124,175,221; 241,151,90; 186,144,192; 255,205,51; 105,142,208; 140,193,104; 50,125,194; 210,96,18; 150,83,159; 204,154,0; 51,90,161; 90,138,57;"{PALETTE_ATTRS}>
  <Series>
    <Series ChartType="StackedColumn" Font="{0}, 9.5px" LabelForeColor="59, 59, 59" CustomProperties="PointWidth=0.75, MaxPixelPointWidth=40"{VALUE_LABEL_FMT} />
  </Series>
  <ChartAreas>
    <ChartArea BorderColor="White" BorderDashStyle="Solid">
      <AxisY{Y_AXIS_ATTRS} LabelAutoFitMinFontSize="8" TitleForeColor="59, 59, 59" TitleFont="{0}, 10.5px" LineColor="165, 172, 181" IntervalAutoMode="VariableCount">
        <MajorGrid LineColor="239, 242, 246" />
        <MajorTickMark LineColor="165, 172, 181" />
        <LabelStyle Font="{0}, 10.5px" ForeColor="59, 59, 59"{Y_AXIS_LABEL_FMT} />
      </AxisY>
      <AxisX{X_AXIS_ATTRS} LabelAutoFitMinFontSize="8" TitleForeColor="59, 59, 59" TitleFont="{0}, 10.5px" LineColor="165, 172, 181" IntervalAutoMode="VariableCount">
        <MajorGrid Enabled="False" />
        <MajorTickMark Enabled="False" />
        <LabelStyle Font="{0}, 10.5px" ForeColor="59, 59, 59"{X_AXIS_LABEL_FMT} />
      </AxisX>
    </ChartArea>
  </ChartAreas>
  <Titles>
    <Title{TITLE_ATTRS} Alignment="TopLeft" DockingOffset="-3" Font="{0}, 13px" ForeColor="0, 0, 0" />
  </Titles>
</Chart>
""";

    // Multi-series — two columns side by side, each a measure (e.g. estimated
    // vs actual revenue). Microsoft uses different colors for the two series;
    // we keep their defaults but expose {SERIES_LABEL_1} / {SERIES_LABEL_2}
    // as LegendText so the legend shows the agent's chosen names.
    private const string MultiSeries = """
<Chart{PALETTE_ATTRS}>
  <Series>
    <Series ChartType="Column" Color="91, 151, 213" LegendText="{SERIES_LABEL_1}" IsVisibleInLegend="True" BackSecondaryColor="74,107,155" Font="{0}, 9.5px" LabelForeColor="59, 59, 59" CustomProperties="PointWidth=0.75, MaxPixelPointWidth=40"{VALUE_LABEL_FMT}>
      <SmartLabelStyle Enabled="True" />
      <Points />
    </Series>
    <Series ChartType="Column" Color="237, 125, 49" LegendText="{SERIES_LABEL_2}" IsVisibleInLegend="True" BackSecondaryColor="126,153,79" Font="{0}, 9.5px" LabelForeColor="59, 59, 59" CustomProperties="PointWidth=0.75, MaxPixelPointWidth=40"{VALUE_LABEL_FMT}>
      <SmartLabelStyle Enabled="True" />
      <Points />
    </Series>
  </Series>
  <ChartAreas>
    <ChartArea BorderColor="White" BorderDashStyle="Solid">
      <AxisY{Y_AXIS_ATTRS} LabelAutoFitMinFontSize="8" TitleForeColor="59, 59, 59" TitleFont="{0}, 10.5px" LineColor="165, 172, 181">
        <MajorGrid LineColor="239, 242, 246" />
        <MajorTickMark LineColor="165, 172, 181" />
        <LabelStyle Font="{0}, 10.5px" ForeColor="59, 59, 59"{Y_AXIS_LABEL_FMT} />
      </AxisY>
      <AxisX{X_AXIS_ATTRS} LabelAutoFitMinFontSize="8" TitleForeColor="59, 59, 59" TitleFont="{0}, 10.5px" LineColor="165, 172, 181">
        <MajorGrid Enabled="False" />
        <MajorTickMark Enabled="False" />
        <LabelStyle Font="{0}, 10.5px" ForeColor="59, 59, 59"{X_AXIS_LABEL_FMT} />
      </AxisX>
    </ChartArea>
  </ChartAreas>
  <Legends>
    <Legend Alignment="Center" LegendStyle="Table" Docking="Bottom" Font="{0}, 11px" ShadowColor="0, 0, 0, 0" ForeColor="59,59,59" />
  </Legends>
  <Titles>
    <Title{TITLE_ATTRS} Alignment="TopLeft" DockingOffset="-3" Font="{0}, 13px" ForeColor="59, 59, 59" />
  </Titles>
</Chart>
""";

    // Comparison stacked — uses two groupby clauses in DataDescription, single
    // <Series> with ChartType=StackedColumn. Each unique value of the
    // secondary groupby becomes a stack segment.
    private const string ComparisonStacked = """
<Chart Palette="None" PaletteCustomColors="91,151,213; 237,125,49; 160,116,166; 255,192,0; 68,114,196; 112,173,71; 37,94,145; 158,72,14; 117,55,125; 153,115,0; 38,68,120; 67,104,43; 124,175,221; 241,151,90; 186,144,192; 255,205,51; 105,142,208; 140,193,104; 50,125,194; 210,96,18; 150,83,159; 204,154,0; 51,90,161; 90,138,57;"{PALETTE_ATTRS}>
  <Series>
    <Series ChartType="StackedColumn" Font="{0}, 9.5px" LabelForeColor="59, 59, 59" CustomProperties="PointWidth=0.75, MaxPixelPointWidth=40"{VALUE_LABEL_FMT}>
    </Series>
  </Series>
  <ChartAreas>
    <ChartArea BorderColor="White" BorderDashStyle="Solid">
      <AxisY{Y_AXIS_ATTRS} LabelAutoFitMinFontSize="8" TitleForeColor="59, 59, 59" TitleFont="{0}, 10.5px" LineColor="165, 172, 181" IntervalAutoMode="VariableCount">
        <MajorGrid LineColor="239, 242, 246" />
        <MajorTickMark LineColor="165, 172, 181" />
        <LabelStyle Font="{0}, 10.5px" ForeColor="59, 59, 59"{Y_AXIS_LABEL_FMT} />
      </AxisY>
      <AxisX{X_AXIS_ATTRS} LabelAutoFitMinFontSize="8" TitleForeColor="59, 59, 59" TitleFont="{0}, 10.5px" LineColor="165, 172, 181" IntervalAutoMode="VariableCount">
        <MajorGrid Enabled="False" />
        <MajorTickMark Enabled="False" />
        <LabelStyle Font="{0}, 10.5px" ForeColor="59, 59, 59"{X_AXIS_LABEL_FMT} />
      </AxisX>
    </ChartArea>
  </ChartAreas>
  <Legends>
    <Legend Alignment="Center" LegendStyle="Table" Docking="Bottom" Font="{0}, 11px" ShadowColor="0, 0, 0, 0" ForeColor="59,59,59" />
  </Legends>
  <Titles>
    <Title{TITLE_ATTRS} Alignment="TopLeft" DockingOffset="-3" Font="{0}, 13px" ForeColor="59, 59, 59" />
  </Titles>
</Chart>
""";

    // 100% stacked variant — bars normalised to 100% so every category looks
    // the same height. Best for share-of-total over time.
    private const string Comparison100Stacked = """
<Chart Palette="None" PaletteCustomColors="149,189,66; 197,56,52; 55,118,193; 117,82,160; 49,171,204; 255,136,35; 168,203,104; 209,98,96; 97,142,206; 142,116,178; 93,186,215; 255,155,83"{PALETTE_ATTRS}>
  <Series>
    <Series ChartType="StackedBar100" Font="{0}, 9.5px" LabelForeColor="59, 59, 59" CustomProperties="PointWidth=0.75, MaxPixelPointWidth=40"{VALUE_LABEL_FMT}>
      <SmartLabelStyle Enabled="True" />{COLOR_RULES}
    </Series>
  </Series>
  <ChartAreas>
    <ChartArea BorderColor="White" BorderDashStyle="Solid">
      <AxisY{Y_AXIS_ATTRS} LabelAutoFitMinFontSize="8" TitleForeColor="59, 59, 59" TitleFont="{0}, 10.5px" LineColor="165, 172, 181" IntervalAutoMode="VariableCount">
        <MajorGrid LineColor="239, 242, 246" />
        <MajorTickMark LineColor="165, 172, 181" />
        <LabelStyle Font="{0}, 10.5px" ForeColor="59, 59, 59"{Y_AXIS_LABEL_FMT} />
      </AxisY>
      <AxisX{X_AXIS_ATTRS} LabelAutoFitMinFontSize="8" TitleForeColor="59, 59, 59" TitleFont="{0}, 10.5px" LineColor="165, 172, 181" IntervalAutoMode="VariableCount">
        <MajorGrid Enabled="False" />
        <MajorTickMark Enabled="False" />
        <LabelStyle Font="{0}, 10.5px" ForeColor="59, 59, 59"{X_AXIS_LABEL_FMT} />
      </AxisX>
    </ChartArea>
  </ChartAreas>
  <Titles>
    <Title{TITLE_ATTRS} Alignment="TopLeft" DockingOffset="-3" Font="{0}, 13px" ForeColor="0, 0, 0" />
  </Titles>
</Chart>
""";
}
