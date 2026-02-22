using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateModelDrivenAppToolTests
{
    private readonly PlanCreateModelDrivenAppTool _tool = new();

    [Fact]
    public async Task PlanCreateModelDrivenApp_ValidInput_ReturnsValidPlan()
    {
        var plan = await _tool.PlanCreateModelDrivenAppAsync(
            appName: "Field Service Hub",
            appUniqueName: "cr123_fieldservicehub",
            description: "Field service management app",
            publisherPrefix: "cr123",
            siteMapDefinition: "Operations:Operations:WorkMgmt:Work Management:cr123_workorder;cr123_task",
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("Field Service Hub", plan.AppName);
        Assert.Equal("cr123_fieldservicehub", plan.AppUniqueName);
        Assert.NotEmpty(plan.SiteMapXml);
        Assert.NotEmpty(plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanCreateModelDrivenApp_SiteMapContainsEntities()
    {
        var plan = await _tool.PlanCreateModelDrivenAppAsync(
            appName: "Project App",
            appUniqueName: "cr123_projectapp",
            description: "Project management",
            publisherPrefix: "cr123",
            siteMapDefinition: "Main:Main:Projects:Projects:cr123_project;cr123_task",
            CancellationToken.None);

        Assert.Contains("cr123_project", plan.SiteMapXml);
        Assert.Contains("cr123_task", plan.SiteMapXml);
        Assert.Contains("SiteMap", plan.SiteMapXml);
        Assert.Contains("Area", plan.SiteMapXml);
        Assert.Contains("SubArea", plan.SiteMapXml);
    }

    [Fact]
    public async Task PlanCreateModelDrivenApp_SpacesInUniqueName_Warning()
    {
        var plan = await _tool.PlanCreateModelDrivenAppAsync(
            appName: "My App",
            appUniqueName: "cr123 my app",
            description: "Test",
            publisherPrefix: "cr123",
            siteMapDefinition: "Main:Main:Group:Group:cr123_table",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("spaces"));
    }

    [Fact]
    public async Task PlanCreateModelDrivenApp_MissingPrefix_Warning()
    {
        var plan = await _tool.PlanCreateModelDrivenAppAsync(
            appName: "My App",
            appUniqueName: "myapp",
            description: "Test",
            publisherPrefix: "cr123",
            siteMapDefinition: "Main:Main:Group:Group:cr123_table",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("prefix"));
    }

    [Fact]
    public async Task PlanCreateModelDrivenApp_EmptySiteMap_Warning()
    {
        var plan = await _tool.PlanCreateModelDrivenAppAsync(
            appName: "Empty App",
            appUniqueName: "cr123_emptyapp",
            description: "Test",
            publisherPrefix: "cr123",
            siteMapDefinition: "",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("empty navigation"));
    }

    [Fact]
    public async Task PlanCreateModelDrivenApp_RiskLevel_IsOrange()
    {
        var plan = await _tool.PlanCreateModelDrivenAppAsync(
            appName: "Test App",
            appUniqueName: "cr123_testapp",
            description: "Test",
            publisherPrefix: "cr123",
            siteMapDefinition: "Main:Main:Group:Group:cr123_table",
            CancellationToken.None);

        Assert.Equal("orange", plan.RiskLevel);
    }

    [Fact]
    public async Task PlanCreateModelDrivenApp_PayloadContainsRequiredProperties()
    {
        var plan = await _tool.PlanCreateModelDrivenAppAsync(
            appName: "Test App",
            appUniqueName: "cr123_testapp",
            description: "Test",
            publisherPrefix: "cr123",
            siteMapDefinition: "Main:Main:Group:Group:cr123_table",
            CancellationToken.None);

        Assert.Contains("uniquename", plan.WebApiPayloadJson);
        Assert.Contains("sitemapxml", plan.WebApiPayloadJson);
        Assert.Contains("clienttype", plan.WebApiPayloadJson);
    }
}
