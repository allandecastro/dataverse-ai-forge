using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class ChartDataDescriptionBuilderTests
{
    [Fact]
    public void Render_SingleMeasureColumn_EmitsExpectedShape()
    {
        var xml = ChartDataDescriptionBuilder.Render(
            tableLogicalName: "opportunity",
            chartType: "column",
            categoryField: "industrycode",
            measureField: "estimatedvalue",
            measureAggregate: "sum",
            secondCategoryField: null,
            secondMeasureField: null,
            secondMeasureAggregate: null,
            dateGrouping: null,
            topN: null);

        Assert.Contains("<datadefinition>", xml);
        Assert.Contains("<entity name=\"opportunity\">", xml);
        Assert.Contains("<attribute name=\"estimatedvalue\" aggregate=\"sum\" alias=\"sum_estimatedvalue\"", xml);
        Assert.Contains("<attribute name=\"industrycode\" groupby=\"true\" alias=\"industrycode\"", xml);
        // Single measure → exactly one <measurecollection>.
        var matches = System.Text.RegularExpressions.Regex.Matches(xml, "<measurecollection>");
        Assert.Single(matches);
    }

    [Fact]
    public void Render_TopN_EmitsCountAndDescendingOrder()
    {
        var xml = ChartDataDescriptionBuilder.Render(
            "opportunity", "bar",
            "customerid", "estimatedvalue", "sum",
            null, null, null, null, topN: 10);

        Assert.Contains("count=\"10\"", xml);
        // Top-N orders by the measure descending.
        Assert.Contains("<order alias=\"sum_estimatedvalue\" descending=\"true\" />", xml);
    }

    [Fact]
    public void Render_DateGrouping_AddsDateGroupingAttr()
    {
        var xml = ChartDataDescriptionBuilder.Render(
            "lead", "line",
            "createdon", "leadid", "countcolumn",
            null, null, null, dateGrouping: "month", topN: null);

        Assert.Contains("dategrouping=\"month\"", xml);
    }

    [Fact]
    public void Render_MultiSeries_EmitsTwoMeasureCollections()
    {
        var xml = ChartDataDescriptionBuilder.Render(
            "opportunity", "multiseries",
            "actualclosedate", "estimatedvalue", "sum",
            null, "actualvalue", "sum",
            "month", null);

        // Both measures should be in the FetchXML.
        Assert.Contains("alias=\"sum_estimatedvalue\"", xml);
        Assert.Contains("alias=\"sum_actualvalue\"", xml);
        // Two measurecollections, one per series.
        var matches = System.Text.RegularExpressions.Regex.Matches(xml, "<measurecollection>");
        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public void Render_Comparison_EmitsTwoGroupbyAttributes()
    {
        var xml = ChartDataDescriptionBuilder.Render(
            "activitypointer", "comparison",
            "activitytypecode", "subject", "count",
            "prioritycode", null, null, null, null);

        // Comparison charts use the "groupby_column" alias for the primary
        // groupby (matching Microsoft's sample naming).
        Assert.Contains("alias=\"groupby_column\"", xml);
        Assert.Contains("alias=\"groupby_secondary\"", xml);
        // Both groupby attributes must carry groupby="true".
        var groupbyMatches = System.Text.RegularExpressions.Regex.Matches(xml, "groupby=\"true\"");
        Assert.True(groupbyMatches.Count >= 2);
    }

    [Fact]
    public void Render_AggregateAndCategoryNames_AreLowercased()
    {
        // Even if the agent passes uppercase, output should be lowercase to match
        // Dataverse's logical-name convention.
        var xml = ChartDataDescriptionBuilder.Render(
            "Opportunity", "column",
            "IndustryCode", "EstimatedValue", "SUM",
            null, null, null, null, null);

        Assert.Contains("<entity name=\"opportunity\">", xml);
        Assert.Contains("<attribute name=\"estimatedvalue\"", xml);
        Assert.Contains("<attribute name=\"industrycode\"", xml);
        Assert.Contains("aggregate=\"sum\"", xml);
        Assert.Contains("alias=\"sum_estimatedvalue\"", xml);
    }

    // ─── filterFetchXml: optional FetchXML <filter> injection ───

    [Fact]
    public void Render_WithFilter_InjectsFilterBetweenAttributesAndOrder()
    {
        var filter = ChartDataDescriptionBuilder.ValidateFilterFetchXml(
            "<filter type=\"and\"><condition attribute=\"ownerid\" operator=\"eq-userid\" /></filter>");
        var xml = ChartDataDescriptionBuilder.Render(
            "opportunity", "column",
            "industrycode", "estimatedvalue", "sum",
            null, null, null, null, null,
            filterFetchXml: filter);

        Assert.Contains("<filter", xml);
        Assert.Contains("operator=\"eq-userid\"", xml);
        // Filter must appear AFTER the groupby attribute and BEFORE the order.
        var filterIdx = xml.IndexOf("<filter", StringComparison.Ordinal);
        var groupbyIdx = xml.IndexOf("<attribute name=\"industrycode\" groupby=\"true\"", StringComparison.Ordinal);
        var orderIdx = xml.IndexOf("<order ", StringComparison.Ordinal);
        Assert.True(groupbyIdx > 0 && filterIdx > groupbyIdx && orderIdx > filterIdx,
            $"Expected order: groupby ({groupbyIdx}) → filter ({filterIdx}) → order ({orderIdx}). XML:\n{xml}");
    }

    [Fact]
    public void Render_WithFilter_DoesNotEscapeOperatorNames()
    {
        // Re-escaping a real filter would mangle Dataverse operators ("eq-userid"
        // would become "eq&#45;userid" or similar). Filter passes through verbatim.
        var filter = ChartDataDescriptionBuilder.ValidateFilterFetchXml(
            "<filter type=\"and\"><condition attribute=\"createdon\" operator=\"this-fiscal-year\" /></filter>");
        var xml = ChartDataDescriptionBuilder.Render(
            "opportunity", "line",
            "createdon", "estimatedvalue", "sum",
            null, null, null, "month", null,
            filterFetchXml: filter);

        Assert.Contains("operator=\"this-fiscal-year\"", xml);
        Assert.DoesNotContain("&lt;filter", xml);
        Assert.DoesNotContain("&quot;eq", xml);
    }

    [Fact]
    public void Render_WithFilter_OnComparisonChart_PreservesBothGroupbys()
    {
        // The filter must not break the secondary groupby that comparison charts need.
        var filter = ChartDataDescriptionBuilder.ValidateFilterFetchXml(
            "<filter type=\"and\"><condition attribute=\"statecode\" operator=\"eq\" value=\"0\" /></filter>");
        var xml = ChartDataDescriptionBuilder.Render(
            "activitypointer", "comparison",
            "activitytypecode", "subject", "count",
            "prioritycode", null, null, null, null,
            filterFetchXml: filter);

        Assert.Contains("alias=\"groupby_column\"", xml);
        Assert.Contains("alias=\"groupby_secondary\"", xml);
        Assert.Contains("attribute=\"statecode\"", xml);
    }

    [Fact]
    public void Render_WithoutFilter_EmitsNoFilterElement()
    {
        // Default behaviour — no filter param, no <filter> in output.
        var xml = ChartDataDescriptionBuilder.Render(
            "opportunity", "column",
            "industrycode", "estimatedvalue", "sum",
            null, null, null, null, null);

        Assert.DoesNotContain("<filter", xml);
    }

    [Fact]
    public void ValidateFilterFetchXml_RejectsInvalidXml()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            ChartDataDescriptionBuilder.ValidateFilterFetchXml("not actually xml"));
        Assert.Contains("not valid XML", ex.Message);
    }

    [Fact]
    public void ValidateFilterFetchXml_RejectsNonFilterRoot()
    {
        // Common agent mistake: wrapping in <fetch> or <conditions>.
        var ex = Assert.Throws<ArgumentException>(() =>
            ChartDataDescriptionBuilder.ValidateFilterFetchXml(
                "<fetch><condition attribute=\"x\" operator=\"eq\" value=\"1\"/></fetch>"));
        Assert.Contains("root element must be <filter>", ex.Message);
    }

    [Fact]
    public void ValidateFilterFetchXml_RejectsNamespacedFilter()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            ChartDataDescriptionBuilder.ValidateFilterFetchXml(
                "<filter xmlns=\"http://example.com\" type=\"and\"></filter>"));
        Assert.Contains("default namespace", ex.Message);
    }

    [Fact]
    public void ValidateFilterFetchXml_RejectsEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            ChartDataDescriptionBuilder.ValidateFilterFetchXml(""));
        Assert.Throws<ArgumentException>(() =>
            ChartDataDescriptionBuilder.ValidateFilterFetchXml("   "));
    }
}
