using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ListAppRolesToolTests
{
    private readonly Mock<IDataverseClient> _mockClient = new();

    [Fact]
    public async Task ListAppRoles_DelegatesToDataverseClient()
    {
        var appId = Guid.NewGuid();
        var bu = Guid.NewGuid();
        var expected = new List<AppModuleRoleInfo>
        {
            new(Guid.NewGuid(), "System Customizer", BusinessUnitId: bu, IsManaged: true),
            new(Guid.NewGuid(), "Salesperson", BusinessUnitId: bu, IsManaged: true),
        };

        _mockClient
            .Setup(c => c.ListAppRolesAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new ListAppRolesTool(_mockClient.Object);
        var result = await tool.ListAppRolesAsync(appId, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("System Customizer", result[0].Name);
    }

    [Fact]
    public async Task ListAppRoles_ReturnsEmptyListWhenAppHasNoRoles()
    {
        var appId = Guid.NewGuid();
        _mockClient
            .Setup(c => c.ListAppRolesAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AppModuleRoleInfo>());

        var tool = new ListAppRolesTool(_mockClient.Object);
        var result = await tool.ListAppRolesAsync(appId, CancellationToken.None);

        Assert.Empty(result);
    }
}
