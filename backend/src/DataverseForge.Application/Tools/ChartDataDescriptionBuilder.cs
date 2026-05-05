using System.Text;
using System.Xml.Linq;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Builds a Dataverse chart's <c>&lt;datadefinition&gt;</c> XML — the FetchXML
/// query plus the categorycollection / measurecollection mapping. Mirrors the
/// shape Microsoft's sample-charts article uses for each chart type.
///
/// Single-measure charts have one &lt;measurecollection&gt; with one &lt;measure&gt;.
/// Multi-series charts have multiple &lt;measurecollection&gt; siblings.
/// Comparison charts add a second groupby attribute alongside the category one.
/// Optional <c>&lt;filter&gt;</c> blocks (predicates the agent built from view
/// metadata or known constants like <c>@loggedinuser</c>) flow in via the
/// <c>filterFetchXml</c> parameter — see <see cref="ValidateFilterFetchXml"/>.
/// </summary>
public static class ChartDataDescriptionBuilder
{
    public static readonly string[] SupportedAggregates =
        ["sum", "count", "countcolumn", "avg", "min", "max"];

    public static readonly string[] SupportedDateGroupings =
        ["day", "week", "month", "quarter", "year", "fiscalperiod", "fiscalyear"];

    /// <summary>
    /// Validates a caller-supplied FetchXML <c>&lt;filter&gt;</c> block. Returns
    /// the canonicalised XML (with consistent indentation / quoting) so the
    /// caller can drop it straight into the chart's data description.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the input fails to parse, has any root other than
    /// <c>&lt;filter&gt;</c>, or carries an XML namespace (FetchXML filters
    /// must be in the default namespace).
    /// </exception>
    public static string ValidateFilterFetchXml(string filterFetchXml)
    {
        if (string.IsNullOrWhiteSpace(filterFetchXml))
            throw new ArgumentException("filterFetchXml is empty.", nameof(filterFetchXml));

        XElement parsed;
        try
        {
            parsed = XElement.Parse(filterFetchXml);
        }
        catch (System.Xml.XmlException xx)
        {
            throw new ArgumentException(
                $"filterFetchXml is not valid XML: {xx.Message}", nameof(filterFetchXml), xx);
        }

        if (!string.Equals(parsed.Name.LocalName, "filter", StringComparison.Ordinal))
            throw new ArgumentException(
                $"filterFetchXml root element must be <filter>, got <{parsed.Name.LocalName}>.",
                nameof(filterFetchXml));

        if (parsed.Name.NamespaceName.Length > 0)
            throw new ArgumentException(
                "filterFetchXml must be in the default namespace (no xmlns attribute).",
                nameof(filterFetchXml));

        return parsed.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Render the DataDescription XML. Caller is responsible for validating
    /// that field names exist on the table — this method only emits the XML.
    /// <paramref name="filterFetchXml"/>, when non-empty, MUST be the result
    /// of <see cref="ValidateFilterFetchXml"/> (or equivalent) — we trust it
    /// blindly and inject it as a child of <c>&lt;entity&gt;</c>.
    /// </summary>
    public static string Render(
        string tableLogicalName,
        string chartType,
        string categoryField,
        string measureField,
        string measureAggregate,
        string? secondCategoryField,
        string? secondMeasureField,
        string? secondMeasureAggregate,
        string? dateGrouping,
        int? topN,
        string? filterFetchXml = null)
    {
        var t = tableLogicalName.ToLowerInvariant();
        var category = categoryField.ToLowerInvariant();
        var measure = measureField.ToLowerInvariant();
        var aggregate = measureAggregate.ToLowerInvariant();
        var measure1Alias = $"{aggregate}_{measure}";
        // The category alias depends on shape:
        //   - simple charts use the field's logical name
        //   - comparison charts use "groupby_column" / "groupby_priority"
        //     (matching Microsoft's sample naming so the chart designer in the
        //     maker portal can round-trip these without rewriting the aliases)
        var isMulti = ChartTemplates.IsMultiSeries(chartType);
        var isComparison = ChartTemplates.IsComparison(chartType);

        var xml = new StringBuilder();
        xml.AppendLine("<datadefinition>");
        xml.AppendLine("  <fetchcollection>");

        var fetchAttrs = new List<string> { "mapping=\"logical\"", "aggregate=\"true\"" };
        if (topN is int top && top > 0)
        {
            fetchAttrs.Add($"count=\"{top}\"");
        }
        xml.AppendLine($"    <fetch {string.Join(' ', fetchAttrs)}>");
        xml.AppendLine($"      <entity name=\"{XmlEscape(t)}\">");

        // Measure 1 — required for every chart.
        xml.AppendLine($"        <attribute name=\"{XmlEscape(measure)}\" aggregate=\"{XmlEscape(aggregate)}\" alias=\"{XmlEscape(measure1Alias)}\" />");

        // Measure 2 — multi-series only.
        string? measure2Alias = null;
        if (isMulti && !string.IsNullOrWhiteSpace(secondMeasureField) && !string.IsNullOrWhiteSpace(secondMeasureAggregate))
        {
            var measure2 = secondMeasureField!.ToLowerInvariant();
            var aggregate2 = secondMeasureAggregate!.ToLowerInvariant();
            measure2Alias = $"{aggregate2}_{measure2}";
            xml.AppendLine($"        <attribute name=\"{XmlEscape(measure2)}\" aggregate=\"{XmlEscape(aggregate2)}\" alias=\"{XmlEscape(measure2Alias)}\" />");
        }

        // Primary groupby (the category axis). Comparison charts use
        // "groupby_column" alias; everything else uses the field's name.
        var primaryAlias = isComparison ? "groupby_column" : category;
        var groupbyAttrs = new List<string>
        {
            $"name=\"{XmlEscape(category)}\"",
            "groupby=\"true\"",
            $"alias=\"{XmlEscape(primaryAlias)}\""
        };
        if (!string.IsNullOrWhiteSpace(dateGrouping))
        {
            groupbyAttrs.Add($"dategrouping=\"{XmlEscape(dateGrouping.ToLowerInvariant())}\"");
        }
        xml.AppendLine($"        <attribute {string.Join(' ', groupbyAttrs)} />");

        // Secondary groupby — comparison charts only.
        if (isComparison && !string.IsNullOrWhiteSpace(secondCategoryField))
        {
            var secondCat = secondCategoryField!.ToLowerInvariant();
            xml.AppendLine($"        <attribute name=\"{XmlEscape(secondCat)}\" groupby=\"true\" alias=\"groupby_secondary\" />");
        }

        // Optional <filter> — sits between attributes and order per the
        // FetchXML schema. Trusted input: the plan tool calls
        // ValidateFilterFetchXml before passing it here, so we do NOT
        // re-escape (re-escaping a real filter would corrupt operator names
        // like "eq-userid").
        if (!string.IsNullOrWhiteSpace(filterFetchXml))
        {
            xml.AppendLine($"        {filterFetchXml}");
        }

        // Order — by the measure descending for top-N charts, by the category
        // ascending otherwise. Microsoft's samples vary; we pick the most
        // useful default.
        var orderAlias = topN is int n && n > 0 ? measure1Alias : primaryAlias;
        var orderDescending = topN is int && topN > 0;
        xml.AppendLine($"        <order alias=\"{XmlEscape(orderAlias)}\" descending=\"{(orderDescending ? "true" : "false")}\" />");

        xml.AppendLine("      </entity>");
        xml.AppendLine("    </fetch>");
        xml.AppendLine("  </fetchcollection>");

        // categorycollection — one <category> with one or two <measurecollection>.
        xml.AppendLine("  <categorycollection>");
        xml.AppendLine("    <category>");
        xml.AppendLine("      <measurecollection>");
        xml.AppendLine($"        <measure alias=\"{XmlEscape(measure1Alias)}\" />");
        xml.AppendLine("      </measurecollection>");
        if (isMulti && measure2Alias is not null)
        {
            xml.AppendLine("      <measurecollection>");
            xml.AppendLine($"        <measure alias=\"{XmlEscape(measure2Alias)}\" />");
            xml.AppendLine("      </measurecollection>");
        }
        xml.AppendLine("    </category>");
        xml.AppendLine("  </categorycollection>");
        xml.AppendLine("</datadefinition>");
        return xml.ToString();
    }

    private static string XmlEscape(string s)
        => string.IsNullOrEmpty(s) ? string.Empty :
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
