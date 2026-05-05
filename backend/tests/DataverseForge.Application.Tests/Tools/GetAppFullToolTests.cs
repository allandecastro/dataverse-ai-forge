using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GetAppFullToolTests
{
    private readonly Mock<IDataverseClient> _mockClient = new();

    [Fact]
    public async Task GetAppFull_DelegatesToDataverseClient_AndMergesAllFanoutResults()
    {
        var appId = Guid.NewGuid();
        var details = new AppModuleDetails(
            appId, "Sales Hub", "msdyn_SalesHub", "", "", 4, 1, 0, 1, true,
            DateTime.UtcNow, Descriptor: null, ConfigXml: null);
        var components = new List<AppModuleComponentRef>
        {
            new("entity", Guid.NewGuid(), "Account", "account"),
            new("systemform", Guid.NewGuid(), "Account Main Form", "account"),
        };
        var siteMap = new SiteMapInfo(Guid.NewGuid(), "Sales Sitemap", "sales_sitemap_unique", "<SiteMap />", IsManaged: true);
        var roles = new List<AppModuleRoleInfo>
        {
            new(Guid.NewGuid(), "Salesperson", BusinessUnitId: null, IsManaged: true),
        };
        var expected = new AppModuleFullDetails(details, components, siteMap, roles);

        _mockClient
            .Setup(c => c.GetAppFullAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new GetAppFullTool(_mockClient.Object);
        var result = await tool.GetAppFullAsync(appId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Sales Hub", result!.App.Name);
        Assert.Equal(2, result.Components.Count);
        Assert.NotNull(result.SiteMap);
        Assert.Equal("Sales Sitemap", result.SiteMap!.SiteMapName);
        Assert.Single(result.Roles);
    }

    [Fact]
    public async Task GetAppFull_ReturnsNullWhenAppMissing()
    {
        var appId = Guid.NewGuid();
        _mockClient
            .Setup(c => c.GetAppFullAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AppModuleFullDetails?)null);

        var tool = new GetAppFullTool(_mockClient.Object);
        var result = await tool.GetAppFullAsync(appId, CancellationToken.None);

        Assert.Null(result);
    }
}
