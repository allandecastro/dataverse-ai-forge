using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GetAppDetailsToolTests
{
    private readonly Mock<IDataverseClient> _mockClient = new();

    [Fact]
    public async Task GetAppDetails_DelegatesToDataverseClient()
    {
        var appId = Guid.NewGuid();
        var expected = new AppModuleDetails(
            appId, "Demo Hub", "demo_DemoHub", "demo app",
            "00000000-0000-0000-0000-000000000abc", 4, 1, 0, 1, false,
            DateTime.UtcNow, Descriptor: "{\"appId\":\"...\"}", ConfigXml: null);

        _mockClient
            .Setup(c => c.GetAppAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new GetAppDetailsTool(_mockClient.Object);
        var result = await tool.GetAppDetailsAsync(appId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Demo Hub", result!.Name);
        Assert.Equal("demo_DemoHub", result.UniqueName);
        _mockClient.Verify(c => c.GetAppAsync(appId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAppDetails_ReturnsNullWhenAppMissing()
    {
        var appId = Guid.NewGuid();
        _mockClient
            .Setup(c => c.GetAppAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AppModuleDetails?)null);

        var tool = new GetAppDetailsTool(_mockClient.Object);
        var result = await tool.GetAppDetailsAsync(appId, CancellationToken.None);

        Assert.Null(result);
    }
}
