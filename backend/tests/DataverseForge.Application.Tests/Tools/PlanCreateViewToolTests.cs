using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateViewToolTests
{
    private static PlanCreateViewTool ToolWithColumns(params string[] columnLogicalNames)
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
                "cr123_workorder", "WorkOrder", "WorkOrder", true, false, "UserOwned",
                "cr123_workorderid", "cr123_name",
                columns, Array.Empty<RelationshipInfo>(), Array.Empty<AlternateKeyInfo>()));

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
        return new PlanCreateViewTool(mock.Object, mockStore.Object, mockCtx.Object);
    }

    [Fact]
    public async Task PlanCreateView_ValidInput_ReturnsValidPlan()
    {
        var tool = ToolWithColumns("cr123_name", "cr123_priority", "cr123_status");
        var plan = await tool.PlanCreateViewAsync(
            tableLogicalName: "cr123_workorder",
            viewName: "Active Work Orders",
            description: "Shows all active work orders",
            columns: "cr123_name,cr123_priority,cr123_status,createdon",
            sortField: "createdon",
            sortDirection: "descending",
            filterCondition: "<condition attribute=\"statecode\" operator=\"eq\" value=\"0\" />",
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("cr123_workorder", plan.TableLogicalName);
        Assert.Equal("Active Work Orders", plan.ViewName);
        Assert.NotEmpty(plan.FetchXml);
        Assert.NotEmpty(plan.LayoutXml);
        Assert.NotEmpty(plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanCreateView_FetchXmlContainsColumnsAndSort()
    {
        var tool = ToolWithColumns("cr123_name", "cr123_status");
        var plan = await tool.PlanCreateViewAsync(
            tableLogicalName: "cr123_project",
            viewName: "All Projects",
            description: "All projects",
            columns: "cr123_name,cr123_status",
            sortField: "cr123_name",
            sortDirection: "ascending",
            filterCondition: "",
            CancellationToken.None);

        Assert.Contains("cr123_name", plan.FetchXml);
        Assert.Contains("cr123_status", plan.FetchXml);
        Assert.Contains("order attribute=\"cr123_name\"", plan.FetchXml);
        Assert.Contains("descending=\"false\"", plan.FetchXml);
    }

    [Fact]
    public async Task PlanCreateView_FetchXmlContainsFilter()
    {
        var tool = ToolWithColumns("cr123_name");
        var plan = await tool.PlanCreateViewAsync(
            tableLogicalName: "cr123_workorder",
            viewName: "Active WOs",
            description: "Active only",
            columns: "cr123_name",
            sortField: "createdon",
            sortDirection: "descending",
            filterCondition: "<condition attribute=\"statecode\" operator=\"eq\" value=\"0\" />",
            CancellationToken.None);

        Assert.Contains("<filter type=\"and\">", plan.FetchXml);
        Assert.Contains("statecode", plan.FetchXml);
    }

    [Fact]
    public async Task PlanCreateView_TooManyColumns_Warning()
    {
        var tool = ToolWithColumns("col1", "col2", "col3", "col4", "col5", "col6", "col7", "col8");
        var plan = await tool.PlanCreateViewAsync(
            tableLogicalName: "cr123_workorder",
            viewName: "Wide View",
            description: "Too many columns",
            columns: "col1,col2,col3,col4,col5,col6,col7,col8",
            sortField: "createdon",
            sortDirection: "descending",
            filterCondition: "",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("5-7 columns"));
    }

    [Fact]
    public async Task PlanCreateView_LayoutXmlContainsColumns()
    {
        var tool = ToolWithColumns("cr123_name", "cr123_duedate");
        var plan = await tool.PlanCreateViewAsync(
            tableLogicalName: "cr123_task",
            viewName: "My Tasks",
            description: "Tasks assigned to me",
            columns: "cr123_name,cr123_duedate",
            sortField: "cr123_duedate",
            sortDirection: "ascending",
            filterCondition: "",
            CancellationToken.None);

        Assert.Contains("cr123_name", plan.LayoutXml);
        Assert.Contains("cr123_duedate", plan.LayoutXml);
    }

    [Fact]
    public async Task PlanCreateView_LayoutXml_UsesRealGuidNotPlaceholder()
    {
        // Regression: layout id used to be literal "{ID}" — Dataverse rejected the
        // savedquery payload with HTTP 400.
        var tool = ToolWithColumns("cr123_name");
        var plan = await tool.PlanCreateViewAsync(
            tableLogicalName: "cr123_workorder",
            viewName: "Test",
            description: "",
            columns: "cr123_name",
            sortField: "createdon",
            sortDirection: "descending",
            filterCondition: "",
            CancellationToken.None);

        // Per the MS Customize Views spec, <row id="..."> must be the primary id
        // attribute name (e.g. "cr123_workorderid"), not a placeholder or GUID.
        Assert.DoesNotContain("id=\"{ID}\"", plan.LayoutXml);
        Assert.Contains("id=\"cr123_workorderid\"", plan.LayoutXml);
        Assert.Contains("jump=\"cr123_name\"", plan.LayoutXml);
    }

    [Fact]
    public async Task PlanCreateView_MissingColumnOnTable_ReturnsRejectedPlan()
    {
        // Regression: agent passed eppc_contactid for a missing lookup. Dataverse
        // rejected the savedquery with HTTP 400. Plan tool now refuses upfront.
        var tool = ToolWithColumns("eppc_giftname", "eppc_type"); // eppc_contactid intentionally missing
        var plan = await tool.PlanCreateViewAsync(
            tableLogicalName: "eppc_gift",
            viewName: "All Gifts with Contact",
            description: "",
            columns: "eppc_giftname,eppc_contactid,eppc_type",
            sortField: "createdon",
            sortDirection: "descending",
            filterCondition: "",
            CancellationToken.None);

        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("REJECTED"));
        Assert.Contains(plan.Warnings, w => w.Contains("eppc_contactid"));
        Assert.Empty(plan.FetchXml);
        Assert.Empty(plan.LayoutXml);
        Assert.Empty(plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanCreateView_SystemFieldsAllowedWithoutMetadataLookup()
    {
        // 'createdon', 'statecode', 'ownerid' etc. are common sort fields. The
        // metadata lookup may not list them, so we whitelist them in the validator.
        var tool = ToolWithColumns("cr123_name"); // ONLY cr123_name; createdon NOT in mock
        var plan = await tool.PlanCreateViewAsync(
            tableLogicalName: "cr123_workorder",
            viewName: "All",
            description: "",
            columns: "cr123_name",
            sortField: "createdon", // system field — must be allowed
            sortDirection: "descending",
            filterCondition: "",
            CancellationToken.None);

        Assert.NotEqual("red", plan.RiskLevel);
        Assert.DoesNotContain(plan.Warnings, w => w.Contains("REJECTED"));
    }

    [Fact]
    public async Task PlanCreateView_RiskLevel_IsGreen()
    {
        var tool = ToolWithColumns("cr123_name");
        var plan = await tool.PlanCreateViewAsync(
            tableLogicalName: "cr123_workorder",
            viewName: "Test View",
            description: "Test",
            columns: "cr123_name",
            sortField: "createdon",
            sortDirection: "descending",
            filterCondition: "",
            CancellationToken.None);

        Assert.Equal("green", plan.RiskLevel);
    }

    [Fact]
    public async Task PlanCreateView_PayloadContainsRequiredProperties()
    {
        var tool = ToolWithColumns("cr123_name");
        var plan = await tool.PlanCreateViewAsync(
            tableLogicalName: "cr123_workorder",
            viewName: "Test View",
            description: "Test",
            columns: "cr123_name",
            sortField: "createdon",
            sortDirection: "descending",
            filterCondition: "",
            CancellationToken.None);

        Assert.Contains("returnedtypecode", plan.WebApiPayloadJson);
        Assert.Contains("fetchxml", plan.WebApiPayloadJson);
        Assert.Contains("layoutxml", plan.WebApiPayloadJson);
        Assert.Contains("cr123_workorder", plan.WebApiPayloadJson);
    }
}
