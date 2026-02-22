using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GetSecurityRolesToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();

    [Fact]
    public async Task GetSecurityRoles_CallsDataverseClient()
    {
        // Arrange
        var expected = new List<SecurityRoleInfo>
        {
            new(Guid.NewGuid(), "System Administrator", true, false, Guid.NewGuid(), 580),
            new(Guid.NewGuid(), "Work Order Contributor", false, true, Guid.NewGuid(), 12)
        }.AsReadOnly();

        _mockDataverseClient
            .Setup(c => c.GetSecurityRolesAsync("custom", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new GetSecurityRolesTool(_mockDataverseClient.Object);

        // Act
        var result = await tool.GetSecurityRolesAsync("custom", CancellationToken.None);

        // Assert
        Assert.Equal(expected, result);
        _mockDataverseClient.Verify(
            c => c.GetSecurityRolesAsync("custom", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSecurityRoles_DefaultsToAll()
    {
        // Arrange
        var expected = new List<SecurityRoleInfo>().AsReadOnly();

        _mockDataverseClient
            .Setup(c => c.GetSecurityRolesAsync("all", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new GetSecurityRolesTool(_mockDataverseClient.Object);

        // Act — pass empty string so it defaults to "all"
        var result = await tool.GetSecurityRolesAsync("", CancellationToken.None);

        // Assert
        _mockDataverseClient.Verify(
            c => c.GetSecurityRolesAsync("all", It.IsAny<CancellationToken>()), Times.Once);
    }
}
