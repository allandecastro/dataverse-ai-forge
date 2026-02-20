using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GetPublishersToolTests
{
    [Fact]
    public async Task GetPublishersAsync_DelegatesToDataverseClient()
    {
        // Arrange
        var expected = new List<PublisherInfo>
        {
            new(Guid.NewGuid(), "Default Publisher", "defaultpublisher", "cr123", 10000),
            new(Guid.NewGuid(), "Contoso", "contoso", "con", 20000)
        }.AsReadOnly();

        var mockClient = new Mock<IDataverseClient>();
        mockClient
            .Setup(c => c.GetPublishersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new GetPublishersTool(mockClient.Object);

        // Act
        var result = await tool.GetPublishersAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, result);
        mockClient.Verify(c => c.GetPublishersAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
