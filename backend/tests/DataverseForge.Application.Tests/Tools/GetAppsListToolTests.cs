using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GetAppsListToolTests
{
    private readonly Mock<IDataverseClient> _mockClient = new();

    [Fact]
    public async Task GetAppsList_DelegatesToDataverseClient_DefaultsToActiveOnly()
    {
        var expected = new List<AppModuleInfo>
        {
            new(Guid.NewGuid(), "Sales Hub", "msdyn_SalesHub", "OOB sales app",
                "00000000-0000-0000-0000-000000000001", 4, 1, 0, 1, true, DateTime.UtcNow),
        };

        _mockClient
            .Setup(c => c.GetAppsListAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new GetAppsListTool(_mockClient.Object);
        var result = await tool.GetAppsListAsync(includeUnpublished: false, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Sales Hub", result[0].Name);
        _mockClient.Verify(c => c.GetAppsListAsync(false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAppsList_ForwardsIncludeUnpublishedFlag()
    {
        _mockClient
            .Setup(c => c.GetAppsListAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppModuleInfo>());

        var tool = new GetAppsListTool(_mockClient.Object);
        await tool.GetAppsListAsync(includeUnpublished: true, CancellationToken.None);

        _mockClient.Verify(c => c.GetAppsListAsync(true, It.IsAny<CancellationToken>()), Times.Once);
    }
}
