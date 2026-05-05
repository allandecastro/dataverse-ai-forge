using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GetSiteMapForAppToolTests
{
    private readonly Mock<IDataverseClient> _mockClient = new();

    [Fact]
    public async Task GetSiteMapForApp_DelegatesToDataverseClient()
    {
        var appId = Guid.NewGuid();
        var expected = new SiteMapInfo(
            Guid.NewGuid(), "Sales Sitemap", "sales_sitemap_unique",
            "<SiteMap><Area /></SiteMap>", IsManaged: false);

        _mockClient
            .Setup(c => c.GetSiteMapForAppAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new GetSiteMapForAppTool(_mockClient.Object);
        var result = await tool.GetSiteMapForAppAsync(appId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Sales Sitemap", result!.SiteMapName);
        Assert.Contains("<SiteMap>", result.SiteMapXml);
    }

    [Fact]
    public async Task GetSiteMapForApp_ReturnsNullWhenAppHasNoSitemap()
    {
        var appId = Guid.NewGuid();
        _mockClient
            .Setup(c => c.GetSiteMapForAppAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SiteMapInfo?)null);

        var tool = new GetSiteMapForAppTool(_mockClient.Object);
        var result = await tool.GetSiteMapForAppAsync(appId, CancellationToken.None);

        Assert.Null(result);
    }
}
