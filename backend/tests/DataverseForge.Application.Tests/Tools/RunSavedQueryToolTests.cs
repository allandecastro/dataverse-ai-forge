using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class RunSavedQueryToolTests
{
    private readonly Mock<IDataverseClient> _client = new();

    private RunSavedQueryTool Build() => new(_client.Object);

    [Fact]
    public async Task RunSavedQuery_ValidGuid_ForwardsToClient()
    {
        var viewId = Guid.NewGuid();
        var fakeResult = new SavedQueryRunResult(
            SavedQueryId: viewId,
            ViewName: "Active Gifts",
            EntityLogicalName: "andras_gift",
            EntitySetName: "andras_gifts",
            FetchXml: "<fetch><entity name=\"andras_gift\"/></fetch>",
            RowCount: 3,
            Rows: new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["andras_giftname"] = "G1" },
                new Dictionary<string, object?> { ["andras_giftname"] = "G2" },
                new Dictionary<string, object?> { ["andras_giftname"] = "G3" },
            });
        _client.Setup(c => c.RunSavedQueryAsync(viewId, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeResult);

        var tool = Build();
        var result = await tool.RunSavedQueryAsync(viewId.ToString(), 10, CancellationToken.None);

        var typed = Assert.IsType<SavedQueryRunResult>(result);
        Assert.Equal("Active Gifts", typed.ViewName);
        Assert.Equal("andras_gift", typed.EntityLogicalName);
        Assert.Equal(3, typed.RowCount);
    }

    [Fact]
    public async Task RunSavedQuery_TopClampedTo500()
    {
        var viewId = Guid.NewGuid();
        _client.Setup(c => c.RunSavedQueryAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SavedQueryRunResult(viewId, "X", "x", "xs", "<fetch/>", 0, Array.Empty<IReadOnlyDictionary<string, object?>>()));

        var tool = Build();
        await tool.RunSavedQueryAsync(viewId.ToString(), top: 9999, CancellationToken.None);

        _client.Verify(c => c.RunSavedQueryAsync(viewId, 500, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunSavedQuery_InvalidGuid_ReturnsErrorEnvelope()
    {
        var tool = Build();
        var result = await tool.RunSavedQueryAsync("not-a-guid", 50, CancellationToken.None);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("\"error\"", json);
        Assert.Contains("not a valid GUID", json);
    }

    [Fact]
    public async Task RunSavedQuery_EmptyGuid_ReturnsErrorEnvelope()
    {
        var tool = Build();
        var result = await tool.RunSavedQueryAsync(Guid.Empty.ToString(), 50, CancellationToken.None);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("\"error\"", json);
        Assert.Contains("not a valid GUID", json);
    }

    [Fact]
    public async Task RunSavedQuery_ClientException_SurfacedAsErrorEnvelope()
    {
        var viewId = Guid.NewGuid();
        _client.Setup(c => c.RunSavedQueryAsync(viewId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("savedquery 404: not found"));
        var tool = Build();

        var result = await tool.RunSavedQueryAsync(viewId.ToString(), 50, CancellationToken.None);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("\"error\"", json);
        Assert.Contains("savedquery 404: not found", json);
    }
}
