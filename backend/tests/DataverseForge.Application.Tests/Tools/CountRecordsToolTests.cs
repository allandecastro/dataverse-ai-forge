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

        var result = await tool.CountRecordsAsync("cr123_categories", null, CancellationToken.None);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("\"count\":42", json);
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

        var result = await tool.CountRecordsAsync("cr123_categories", filter, CancellationToken.None);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("\"count\":7", json);
        _mockDataverseClient.Verify(
            c => c.GetRecordCountAsync("cr123_categories", filter, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Same pattern as the other read tools — without this catch, AIFunctionFactory
    // would surface a generic "Function failed." string and the agent has no error
    // to quote back to the user.
    [Fact]
    public async Task ClientException_SurfacedAsErrorEnvelope()
    {
        _mockDataverseClient
            .Setup(c => c.GetRecordCountAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse 404: entity set not found"));
        var tool = CreateTool();

        var result = await tool.CountRecordsAsync("cr123_categories", null, CancellationToken.None);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("\"error\"", json);
        Assert.Contains("Dataverse 404: entity set not found", json);
    }
}
