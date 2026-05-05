using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GetRecordsToolTests
{
    private readonly Mock<IDataverseClient> _client = new();

    private GetRecordsTool CreateTool() => new(_client.Object);

    [Fact]
    public async Task ForwardsArgumentsToClient()
    {
        var tool = CreateTool();
        var fake = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["contactid"] = Guid.NewGuid().ToString(), ["fullname"] = "A. Person" }
        };
        _client.Setup(c => c.GetRecordsAsync("contacts", "contactid,fullname", null, null, 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fake);

        var result = await tool.GetRecordsAsync("contacts", "contactid,fullname", null, null, 3, CancellationToken.None);

        var rows = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(result);
        Assert.Single(rows);
        _client.Verify(c => c.GetRecordsAsync("contacts", "contactid,fullname", null, null, 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ZeroOrNegativeTop_DefaultsTo5()
    {
        var tool = CreateTool();
        _client.Setup(c => c.GetRecordsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await tool.GetRecordsAsync("contacts", null, null, null, 0, CancellationToken.None);
        await tool.GetRecordsAsync("contacts", null, null, null, -10, CancellationToken.None);

        _client.Verify(c => c.GetRecordsAsync("contacts", null, null, null, 5, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task TopOver50_ClampedTo50()
    {
        var tool = CreateTool();
        _client.Setup(c => c.GetRecordsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await tool.GetRecordsAsync("contacts", null, null, null, 9999, CancellationToken.None);

        _client.Verify(c => c.GetRecordsAsync("contacts", null, null, null, 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Without this catch, AIFunctionFactory turns any client exception into a
    // generic "Function failed." string and the agent tells the user "function
    // failed" instead of the actual cause. The test asserts the error is
    // surfaced verbatim so the prompt's "quote errors" rule has something real
    // to quote.
    [Fact]
    public async Task ClientException_SurfacedAsErrorEnvelope()
    {
        var tool = CreateTool();
        _client.Setup(c => c.GetRecordsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse 401: token expired"));

        var result = await tool.GetRecordsAsync("contacts", null, null, null, 5, CancellationToken.None);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("\"error\"", json);
        Assert.Contains("Dataverse 401: token expired", json);
        Assert.Contains("contacts", json);
    }
}
