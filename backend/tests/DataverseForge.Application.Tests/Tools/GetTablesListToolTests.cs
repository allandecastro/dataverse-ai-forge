using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GetTablesListToolTests
{
    [Fact]
    public async Task GetTablesListAsync_DelegatesToDataverseClientWithCorrectParameters()
    {
        // Arrange
        var expected = new List<TableInfo>
        {
            new("account", "Account", "Account", false, false, "UserOwned",
                "accountid", "name", true, true, false)
        }.AsReadOnly();

        var mockClient = new Mock<IDataverseClient>();
        mockClient
            .Setup(c => c.GetTablesListAsync("custom", null, "account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new GetTablesListTool(mockClient.Object);

        // Act
        var result = await tool.GetTablesListAsync("custom", null, "account", CancellationToken.None);

        // Assert
        Assert.Equal(expected, result);
        mockClient.Verify(c => c.GetTablesListAsync("custom", null, "account", It.IsAny<CancellationToken>()), Times.Once);
    }
}
