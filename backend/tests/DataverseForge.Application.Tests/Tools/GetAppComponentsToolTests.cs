using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GetAppComponentsToolTests
{
    [Fact]
    public async Task GetAppComponents_DelegatesToClient()
    {
        var appId = Guid.NewGuid();
        var expected = new List<AppModuleComponentRef>
        {
            new("entity", Guid.NewGuid(), "Account", "account"),
            new("systemform", Guid.NewGuid(), "Main Form", "account"),
            new("sitemap", Guid.NewGuid(), "msdyn_DemoSitemap", null),
        };

        var client = new Mock<IDataverseClient>();
        client.Setup(c => c.GetAppComponentsAsync(appId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new GetAppComponentsTool(client.Object);
        var result = await tool.GetAppComponentsAsync(appId, CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, r => r.ComponentKind == "entity" && r.LogicalName == "account");
        Assert.Contains(result, r => r.ComponentKind == "sitemap");
        client.Verify(c => c.GetAppComponentsAsync(appId, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAppComponents_ReturnsEmptyWhenAppHasNone()
    {
        var appId = Guid.NewGuid();
        var client = new Mock<IDataverseClient>();
        client.Setup(c => c.GetAppComponentsAsync(appId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AppModuleComponentRef>());

        var tool = new GetAppComponentsTool(client.Object);
        var result = await tool.GetAppComponentsAsync(appId, CancellationToken.None);

        Assert.Empty(result);
    }
}
