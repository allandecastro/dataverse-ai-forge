using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateViewToolTests
{
    private readonly PlanCreateViewTool _tool = new();

    [Fact]
    public async Task PlanCreateView_ValidInput_ReturnsValidPlan()
    {
        var plan = await _tool.PlanCreateViewAsync(
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
        var plan = await _tool.PlanCreateViewAsync(
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
        var plan = await _tool.PlanCreateViewAsync(
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
        var plan = await _tool.PlanCreateViewAsync(
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
        var plan = await _tool.PlanCreateViewAsync(
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
    public async Task PlanCreateView_RiskLevel_IsGreen()
    {
        var plan = await _tool.PlanCreateViewAsync(
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
        var plan = await _tool.PlanCreateViewAsync(
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
