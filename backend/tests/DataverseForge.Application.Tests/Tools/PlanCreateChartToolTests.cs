using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateChartToolTests
{
    /// <summary>
    /// Builds the tool with a mocked IDataverseClient that reports the given
    /// columns as existing on the table — same pattern as
    /// PlanCreateFormToolTests.ToolWithColumns.
    /// </summary>
    private static PlanCreateChartTool ToolWithColumns(params string[] columnLogicalNames)
        => ToolWithColumnsAndOptionSet(null, columnLogicalNames);

    /// <summary>
    /// Same as <see cref="ToolWithColumns"/> but also stubs
    /// <c>GetLocalOptionSetOptionsAsync</c> with a fixed option set so tests
    /// can exercise the colorByOptionSet sugar path. Pass null to leave the
    /// optionset stub returning empty.
    /// </summary>
    private static PlanCreateChartTool ToolWithColumnsAndOptionSet(
        IReadOnlyList<OptionInfo>? options,
        params string[] columnLogicalNames)
    {
        var mock = new Mock<IDataverseClient>();
        var columns = columnLogicalNames
            .Select(n => new ColumnInfo(
                LogicalName: n,
                DisplayName: n,
                AttributeType: "StringType",
                IsCustom: true,
                RequiredLevel: "None",
                MaxLength: 200,
                IsSearchable: true))
            .ToArray();
        mock.Setup(c => c.GetTableDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableDetails(
                "x", "X", "X", true, false, "UserOwned", "xid", "name",
                columns, Array.Empty<RelationshipInfo>(), Array.Empty<AlternateKeyInfo>()));
        mock.Setup(c => c.GetLocalOptionSetOptionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(options ?? Array.Empty<OptionInfo>());

        var stored = new Dictionary<string, ProjectState>();
        var mockStore = new Mock<IProjectStateStore>();
        mockStore.Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
            {
                if (!stored.TryGetValue(id, out var s)) { s = new ProjectState(); stored[id] = s; }
                return s;
            });
        mockStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Callback((string id, ProjectState s, CancellationToken _) => stored[id] = s)
            .Returns(Task.CompletedTask);
        var mockCtx = new Mock<ISessionContext>();
        mockCtx.SetupGet(c => c.SessionId).Returns("test-session");
        return new PlanCreateChartTool(mock.Object, mockStore.Object, mockCtx.Object);
    }

    [Fact]
    public async Task PlanCreateChart_Column_HappyPath_ReturnsValidPlan()
    {
        var tool = ToolWithColumns("industrycode", "name");
        var plan = await tool.PlanCreateChartAsync(
            tableLogicalName: "account",
            chartName: "Accounts by Industry",
            description: "How many accounts per industry",
            chartType: "column",
            categoryField: "industrycode",
            measureField: "name",
            measureAggregate: "count",
            secondCategoryField: "",
            secondMeasureField: "",
            secondMeasureAggregate: "",
            dateGrouping: "",
            topN: 0,
            chartTitle: "Accounts by Industry",
            xAxisTitle: "",
            yAxisTitle: "",
            seriesLabel1: "",
            seriesLabel2: "",
            paletteCustomColors: "",
            cancellationToken: CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.StartsWith("create_chart_", plan.PlanId);
        Assert.Contains("<datadefinition>", plan.DataDescriptionXml);
        Assert.Contains("ChartType=\"Column\"", plan.PresentationDescriptionXml);
        // OData payload must include the two XML blobs + primaryentitytypecode.
        Assert.Contains("\"primaryentitytypecode\": \"account\"", plan.WebApiPayloadJson);
        Assert.Contains("\"datadescription\":", plan.WebApiPayloadJson);
        Assert.Contains("\"presentationdescription\":", plan.WebApiPayloadJson);
        Assert.Contains("\"type\": 1", plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanCreateChart_TopN_HasFetchCount()
    {
        var tool = ToolWithColumns("customerid", "estimatedvalue");
        var plan = await tool.PlanCreateChartAsync(
            "opportunity", "Top 10 Customers", "", "bar",
            "customerid", "estimatedvalue", "sum",
            "", "", "", "", topN: 10,
            "Top 10 Customers", "", "", "", "", "",
            CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.Contains("count=\"10\"", plan.DataDescriptionXml);
    }

    [Fact]
    public async Task PlanCreateChart_MultiSeries_RequiresSecondMeasure()
    {
        var tool = ToolWithColumns("actualclosedate", "estimatedvalue");
        var plan = await tool.PlanCreateChartAsync(
            "opportunity", "Estimated vs Actual", "", "multiseries",
            "actualclosedate", "estimatedvalue", "sum",
            "", "", "",  // missing secondMeasureField + Aggregate
            "month", 0, "", "", "", "", "", "",
            CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("multiseries", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanCreateChart_Comparison_RequiresSecondCategory()
    {
        var tool = ToolWithColumns("activitytypecode", "subject");
        var plan = await tool.PlanCreateChartAsync(
            "activitypointer", "Activities", "", "comparison",
            "activitytypecode", "subject", "count",
            "",  // missing secondCategoryField
            "", "", "", 0, "", "", "", "", "", "",
            CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("secondCategoryField"));
    }

    [Fact]
    public async Task PlanCreateChart_Pie_RejectsSecondMeasure()
    {
        // Pie/funnel/doughnut don't support multi-series — the tool should
        // catch this BEFORE shipping a doomed payload to Dataverse.
        var tool = ToolWithColumns("leadqualitycode", "fullname", "rating");
        var plan = await tool.PlanCreateChartAsync(
            "lead", "Leads by Rating", "", "pie",
            "leadqualitycode", "fullname", "count",
            "",
            "rating", "count",  // shouldn't be set for pie
            "", 0, "", "", "", "", "", "",
            CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("single measure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanCreateChart_UnsupportedChartType_Rejected()
    {
        var tool = ToolWithColumns("industrycode", "name");
        var plan = await tool.PlanCreateChartAsync(
            "account", "X", "", "hologram",
            "industrycode", "name", "count",
            "", "", "", "", 0, "", "", "", "", "", "",
            CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("Unsupported chartType"));
    }

    [Fact]
    public async Task PlanCreateChart_UnsupportedAggregate_Rejected()
    {
        var tool = ToolWithColumns("industrycode", "name");
        var plan = await tool.PlanCreateChartAsync(
            "account", "X", "", "column",
            "industrycode", "name", "median",  // not in our enum
            "", "", "", "", 0, "", "", "", "", "", "",
            CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("measureAggregate"));
    }

    [Fact]
    public async Task PlanCreateChart_FieldMissingFromTable_Rejected()
    {
        var tool = ToolWithColumns("industrycode");  // measure 'estimatedvalue' missing
        var plan = await tool.PlanCreateChartAsync(
            "account", "X", "", "column",
            "industrycode", "estimatedvalue", "sum",
            "", "", "", "", 0, "", "", "", "", "", "",
            CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("estimatedvalue"));
    }

    [Fact]
    public async Task PlanCreateChart_PrefixedStandardField_RejectionSuggestsBareName()
    {
        // Real-session failure: agent passed `eppc_statecode` as categoryField
        // for a chart on eppc_gift, expecting a publisher-prefixed copy. None
        // exists. The rejection should explicitly tell the agent to drop the
        // prefix and re-call with `statecode`.
        var tool = ToolWithColumns("statecode", "statuscode", "ownerid", "eppc_giftid");
        var plan = await tool.PlanCreateChartAsync(
            "eppc_gift", "Gifts by State", "", "pie",
            "eppc_statecode", "eppc_giftid", "countcolumn",
            "", "", "", "", 0, "", "", "", "", "", "",
            CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w =>
            w.Contains("STANDARD-FIELD MISTAKE", StringComparison.OrdinalIgnoreCase)
            && w.Contains("'eppc_statecode' → use 'statecode'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlanCreateChart_PaletteFlowsToPresentation()
    {
        var tool = ToolWithColumns("industrycode", "name");
        var plan = await tool.PlanCreateChartAsync(
            "account", "X", "", "column",
            "industrycode", "name", "count",
            "", "", "", "", 0, "", "", "", "", "",
            paletteCustomColors: "91,151,213; 237,125,49",
            cancellationToken: CancellationToken.None);

        Assert.Contains("PaletteCustomColors=\"91,151,213; 237,125,49\"", plan.PresentationDescriptionXml);
        Assert.Contains("CustomColorOverride=\"true\"", plan.PresentationDescriptionXml);
    }

    [Fact]
    public async Task PlanCreateChart_FilterFetchXml_FlowsToDataDescription()
    {
        var tool = ToolWithColumns("industrycode", "name");
        var plan = await tool.PlanCreateChartAsync(
            tableLogicalName: "account",
            chartName: "My Industries",
            description: "",
            chartType: "column",
            categoryField: "industrycode",
            measureField: "name",
            measureAggregate: "count",
            secondCategoryField: "",
            secondMeasureField: "",
            secondMeasureAggregate: "",
            dateGrouping: "",
            topN: 0,
            chartTitle: "",
            xAxisTitle: "",
            yAxisTitle: "",
            seriesLabel1: "",
            seriesLabel2: "",
            paletteCustomColors: "",
            cancellationToken: CancellationToken.None,
            filterFetchXml: "<filter type=\"and\"><condition attribute=\"ownerid\" operator=\"eq-userid\"/></filter>");

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.Contains("operator=\"eq-userid\"", plan.DataDescriptionXml);
    }

    [Fact]
    public async Task PlanCreateChart_ColorRulesJson_WarnsButDoesNotEmitXml()
    {
        // Color rules are validated for shape but never emitted — Dataverse
        // rejects the <Conditional>/<ColorRule> child of <Series.Color> with
        // HTTP 400 0x8004e016 (real session 2026-05-05). The plan must succeed
        // and surface ChartTemplates.ColorRulesNotSupportedReason as a warning
        // so the agent can pivot to a form-script suggestion.
        var tool = ToolWithColumns("prioritycode", "name");
        var plan = await tool.PlanCreateChartAsync(
            tableLogicalName: "incident",
            chartName: "Cases by Priority",
            description: "",
            chartType: "pie",
            categoryField: "prioritycode",
            measureField: "name",
            measureAggregate: "count",
            secondCategoryField: "",
            secondMeasureField: "",
            secondMeasureAggregate: "",
            dateGrouping: "",
            topN: 0,
            chartTitle: "",
            xAxisTitle: "",
            yAxisTitle: "",
            seriesLabel1: "",
            seriesLabel2: "",
            paletteCustomColors: "",
            cancellationToken: CancellationToken.None,
            colorRulesJson: "[{\"when\":\"1\",\"color\":\"#D62728\"},{\"when\":\"2\",\"color\":\"#2CA02C\"}]");

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.DoesNotContain("<ColorRule>", plan.PresentationDescriptionXml);
        Assert.DoesNotContain("<Conditional>", plan.PresentationDescriptionXml);
        Assert.Contains(plan.Warnings, w => w.Contains("aren't supported by Dataverse", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanCreateChart_ColorByOptionSet_WarnsAndUsesDefault()
    {
        // colorByOptionSet shares the same root cause — even when option chips
        // have colors set, we can't emit them into the chart XML, so the plan
        // surfaces a "not supported" warning and uses the default palette.
        var options = new List<OptionInfo>
        {
            new(1, "High",   null, "#D62728"),
            new(2, "Medium", null, "#FFC000"),
            new(3, "Low",    null, "#2CA02C"),
        };
        var tool = ToolWithColumnsAndOptionSet(options, "prioritycode", "name");

        var plan = await tool.PlanCreateChartAsync(
            tableLogicalName: "incident",
            chartName: "By Priority",
            description: "",
            chartType: "pie",
            categoryField: "prioritycode",
            measureField: "name",
            measureAggregate: "count",
            secondCategoryField: "",
            secondMeasureField: "",
            secondMeasureAggregate: "",
            dateGrouping: "",
            topN: 0,
            chartTitle: "",
            xAxisTitle: "",
            yAxisTitle: "",
            seriesLabel1: "",
            seriesLabel2: "",
            paletteCustomColors: "",
            cancellationToken: CancellationToken.None,
            colorByOptionSet: "prioritycode");

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.DoesNotContain("214, 39, 40", plan.PresentationDescriptionXml);
        Assert.DoesNotContain("<ColorRule>", plan.PresentationDescriptionXml);
        Assert.Contains(plan.Warnings, w =>
            w.Contains("colorByOptionSet 'prioritycode'", StringComparison.OrdinalIgnoreCase) &&
            w.Contains("aren't supported by Dataverse", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanCreateChart_InvalidColorRulesJson_Rejected()
    {
        var tool = ToolWithColumns("prioritycode", "name");
        var plan = await tool.PlanCreateChartAsync(
            tableLogicalName: "incident",
            chartName: "X",
            description: "",
            chartType: "pie",
            categoryField: "prioritycode",
            measureField: "name",
            measureAggregate: "count",
            secondCategoryField: "",
            secondMeasureField: "",
            secondMeasureAggregate: "",
            dateGrouping: "",
            topN: 0,
            chartTitle: "",
            xAxisTitle: "",
            yAxisTitle: "",
            seriesLabel1: "",
            seriesLabel2: "",
            paletteCustomColors: "",
            cancellationToken: CancellationToken.None,
            colorRulesJson: "[]"); // empty array — should reject

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("colorRulesJson is invalid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanCreateChart_InvalidFilterFetchXml_Rejected()
    {
        // Common agent mistake: passing a <fetch> wrapper instead of bare <filter>.
        var tool = ToolWithColumns("industrycode", "name");
        var plan = await tool.PlanCreateChartAsync(
            tableLogicalName: "account",
            chartName: "X",
            description: "",
            chartType: "column",
            categoryField: "industrycode",
            measureField: "name",
            measureAggregate: "count",
            secondCategoryField: "",
            secondMeasureField: "",
            secondMeasureAggregate: "",
            dateGrouping: "",
            topN: 0,
            chartTitle: "",
            xAxisTitle: "",
            yAxisTitle: "",
            seriesLabel1: "",
            seriesLabel2: "",
            paletteCustomColors: "",
            cancellationToken: CancellationToken.None,
            filterFetchXml: "<fetch><condition attribute=\"x\" operator=\"eq\" value=\"1\"/></fetch>");

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("filterFetchXml is invalid", StringComparison.OrdinalIgnoreCase));
    }
}
