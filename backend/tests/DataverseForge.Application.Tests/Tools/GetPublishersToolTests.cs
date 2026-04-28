using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GetPublishersToolTests
{
    [Fact]
    public async Task GetPublishersAsync_DelegatesToDataverseClient()
    {
        var expected = new List<PublisherInfo>
        {
            new(Guid.NewGuid(), "Default Publisher", "defaultpublisher", "cr123", 10000),
            new(Guid.NewGuid(), "Contoso", "contoso", "con", 20000)
        }.AsReadOnly();

        var mockClient = new Mock<IDataverseClient>();
        mockClient
            .Setup(c => c.GetPublishersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new GetPublishersTool(mockClient.Object, NullLogger<GetPublishersTool>.Instance);

        var result = await tool.GetPublishersAsync(CancellationToken.None);

        Assert.Contains("Contoso", result);
        Assert.Contains("Default Publisher", result);
        mockClient.Verify(c => c.GetPublishersAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPublishersAsync_ReturnsErrorString_WhenClientThrows()
    {
        var mockClient = new Mock<IDataverseClient>();
        mockClient
            .Setup(c => c.GetPublishersAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("404 Not Found"));

        var tool = new GetPublishersTool(mockClient.Object, NullLogger<GetPublishersTool>.Instance);

        var result = await tool.GetPublishersAsync(CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("404 Not Found", result);
    }
}
