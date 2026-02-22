using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class CountRecordsToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();

    private CountRecordsTool CreateTool() => new(_mockDataverseClient.Object);

    [Fact]
    public async Task CallsGetRecordCountAsync()
    {
        _mockDataverseClient
            .Setup(c => c.GetRecordCountAsync("cr123_categories", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);
        var tool = CreateTool();

        var count = await tool.CountRecordsAsync("cr123_categories", null, CancellationToken.None);

        Assert.Equal(42, count);
        _mockDataverseClient.Verify(
            c => c.GetRecordCountAsync("cr123_categories", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PassesFilter()
    {
        var filter = "contains(cr123_name,'test')";
        _mockDataverseClient
            .Setup(c => c.GetRecordCountAsync("cr123_categories", filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
        var tool = CreateTool();

        var count = await tool.CountRecordsAsync("cr123_categories", filter, CancellationToken.None);

        Assert.Equal(7, count);
        _mockDataverseClient.Verify(
            c => c.GetRecordCountAsync("cr123_categories", filter, It.IsAny<CancellationToken>()), Times.Once);
    }
}
