using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GetSolutionsListToolTests
{
    [Fact]
    public async Task GetSolutionsListAsync_DelegatesToDataverseClient()
    {
        // Arrange
        var expected = new List<SolutionInfo>
        {
            new(
                SolutionId: Guid.NewGuid(),
                UniqueName: "ContosoSales",
                DisplayName: "Contoso Sales",
                Version: "1.0.0.0",
                PublisherName: "contoso",
                IsManaged: false,
                ComponentCount: 42,
                ModifiedOn: DateTime.UtcNow)
        };

        var mockClient = new Mock<IDataverseClient>();
        mockClient
            .Setup(c => c.GetSolutionsListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new GetSolutionsListTool(mockClient.Object);

        // Act
        var result = await tool.GetSolutionsListAsync(CancellationToken.None);

        // Assert
        Assert.Single(result);
        Assert.Equal(expected[0].UniqueName, result[0].UniqueName);
        mockClient.Verify(c => c.GetSolutionsListAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
