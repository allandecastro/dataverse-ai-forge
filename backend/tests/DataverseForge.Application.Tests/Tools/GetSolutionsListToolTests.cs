using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GetSolutionsListToolTests
{
    [Fact]
    public async Task GetSolutionsListAsync_DelegatesToDataverseClient()
    {
        var expected = new List<SolutionInfo>
        {
            new(
                SolutionId: Guid.NewGuid(),
                UniqueName: "ContosoSales",
                DisplayName: "Contoso Sales",
                Version: "1.0.0.0",
                PublisherName: "contoso",
                PublisherId: Guid.NewGuid(),
                PublisherUniqueName: "contoso",
                PublisherPrefix: "con",
                IsManaged: false,
                ComponentCount: 42,
                ModifiedOn: DateTime.UtcNow)
        };

        var mockClient = new Mock<IDataverseClient>();
        mockClient
            .Setup(c => c.GetSolutionsListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new GetSolutionsListTool(mockClient.Object, NullLogger<GetSolutionsListTool>.Instance);

        var result = await tool.GetSolutionsListAsync(CancellationToken.None);

        Assert.Contains("ContosoSales", result);
        Assert.Contains("Contoso Sales", result);
        mockClient.Verify(c => c.GetSolutionsListAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSolutionsListAsync_ReturnsErrorString_WhenClientThrows()
    {
        var mockClient = new Mock<IDataverseClient>();
        mockClient
            .Setup(c => c.GetSolutionsListAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("403 Forbidden"));

        var tool = new GetSolutionsListTool(mockClient.Object, NullLogger<GetSolutionsListTool>.Instance);

        var result = await tool.GetSolutionsListAsync(CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("403 Forbidden", result);
    }
}
