using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateDashboardToolTests
{
    private readonly PlanCreateDashboardTool _tool = new();

    [Fact]
    public async Task PlanCreateDashboard_ValidInput_ReturnsValidPlan()
    {
        var plan = await _tool.PlanCreateDashboardAsync(
            dashboardName: "Work Order Overview",
            description: "Overview dashboard for work orders",
            columnCount: 2,
            viewComponents: "cr123_workorder:Active Work Orders,cr123_task:My Tasks",
            chartComponents: "cr123_workorder:Orders by Status",
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("Work Order Overview", plan.DashboardName);
        Assert.NotEmpty(plan.FormXml);
        Assert.NotEmpty(plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanCreateDashboard_FormXmlContainsComponents()
    {
        var plan = await _tool.PlanCreateDashboardAsync(
            dashboardName: "Project Dashboard",
            description: "Project overview",
            columnCount: 2,
            viewComponents: "cr123_project:All Projects",
            chartComponents: "cr123_project:Projects by Status",
            CancellationToken.None);

        Assert.Contains("cr123_project", plan.FormXml);
        Assert.Contains("ChartGridMode", plan.FormXml);
    }

    [Fact]
    public async Task PlanCreateDashboard_TooManyComponents_Warning()
    {
        var plan = await _tool.PlanCreateDashboardAsync(
            dashboardName: "Big Dashboard",
            description: "Too many components",
            columnCount: 3,
            viewComponents: "t1:v1,t2:v2,t3:v3,t4:v4",
            chartComponents: "t5:c1,t6:c2,t7:c3",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("6"));
    }

    [Fact]
    public async Task PlanCreateDashboard_InvalidColumnCount_DefaultsToTwo()
    {
        var plan = await _tool.PlanCreateDashboardAsync(
            dashboardName: "Test",
            description: "Test",
            columnCount: 5,
            viewComponents: "cr123_task:All Tasks",
            chartComponents: "",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("1-3"));
    }

    [Fact]
    public async Task PlanCreateDashboard_RiskLevel_IsGreen()
    {
        var plan = await _tool.PlanCreateDashboardAsync(
            dashboardName: "Test",
            description: "Test",
            columnCount: 2,
            viewComponents: "cr123_task:All Tasks",
            chartComponents: "",
            CancellationToken.None);

        Assert.Equal("green", plan.RiskLevel);
    }

    [Fact]
    public async Task PlanCreateDashboard_PayloadContainsFormXml()
    {
        var plan = await _tool.PlanCreateDashboardAsync(
            dashboardName: "Test Dashboard",
            description: "Test",
            columnCount: 2,
            viewComponents: "cr123_task:Tasks",
            chartComponents: "",
            CancellationToken.None);

        Assert.Contains("formxml", plan.WebApiPayloadJson);
        Assert.Contains("Test Dashboard", plan.WebApiPayloadJson);
    }
}
