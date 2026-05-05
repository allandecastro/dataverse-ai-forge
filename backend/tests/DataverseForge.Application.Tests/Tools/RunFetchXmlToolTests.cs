using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class RunFetchXmlToolTests
{
    private readonly Mock<IDataverseClient> _client = new();

    private RunFetchXmlTool Build() => new(_client.Object);

    [Fact]
    public async Task RunFetchXml_ValidFetch_ForwardsToClientAndReturnsRows()
    {
        var fakeRows = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["contactid"] = Guid.NewGuid().ToString(), ["fullname"] = "Alice" },
            new Dictionary<string, object?> { ["contactid"] = Guid.NewGuid().ToString(), ["fullname"] = "Bob" }
        };
        _client.Setup(c => c.RunFetchXmlAsync("contacts", It.IsAny<string>(), 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeRows);

        var tool = Build();
        var fetch = "<fetch><entity name=\"contact\"><attribute name=\"contactid\"/><attribute name=\"fullname\"/></entity></fetch>";
        var result = await tool.RunFetchXmlAsync("contacts", fetch, 10, CancellationToken.None);

        var rows = Assert.IsAssignableFrom<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(result);
        Assert.Equal(2, rows.Count);
        _client.Verify(c => c.RunFetchXmlAsync("contacts", fetch, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunFetchXml_TopClampedTo500()
    {
        _client.Setup(c => c.RunFetchXmlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IReadOnlyDictionary<string, object?>>());

        var tool = Build();
        await tool.RunFetchXmlAsync("contacts",
            "<fetch><entity name=\"contact\"></entity></fetch>",
            top: 9999, CancellationToken.None);

        _client.Verify(c => c.RunFetchXmlAsync("contacts", It.IsAny<string>(), 500, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunFetchXml_TopZero_DefaultsTo50()
    {
        _client.Setup(c => c.RunFetchXmlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IReadOnlyDictionary<string, object?>>());

        var tool = Build();
        await tool.RunFetchXmlAsync("contacts",
            "<fetch><entity name=\"contact\"></entity></fetch>",
            top: 0, CancellationToken.None);

        _client.Verify(c => c.RunFetchXmlAsync("contacts", It.IsAny<string>(), 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Validation errors now go through the same try/catch envelope so the agent
    // sees the message verbatim instead of "function failed".

    [Fact]
    public async Task RunFetchXml_RejectsInvalidXml()
    {
        var tool = Build();
        var result = await tool.RunFetchXmlAsync("contacts", "not valid xml", 50, CancellationToken.None);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("\"error\"", json);
        Assert.Contains("not valid XML", json);
        _client.Verify(c => c.RunFetchXmlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunFetchXml_RejectsNonFetchRoot()
    {
        var tool = Build();
        var result = await tool.RunFetchXmlAsync("contacts",
            "<query><entity name=\"contact\"/></query>",
            50, CancellationToken.None);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("\"error\"", json);
        Assert.Contains("root element must be \\u003Cfetch\\u003E", json);
    }

    [Fact]
    public async Task RunFetchXml_RejectsNamespacedFetch()
    {
        var tool = Build();
        var result = await tool.RunFetchXmlAsync("contacts",
            "<fetch xmlns=\"http://example.com\"><entity name=\"contact\"/></fetch>",
            50, CancellationToken.None);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("\"error\"", json);
        Assert.Contains("default namespace", json);
    }

    [Fact]
    public async Task RunFetchXml_EmptyEntitySetName_ReturnsError()
    {
        var tool = Build();
        var result = await tool.RunFetchXmlAsync("",
            "<fetch><entity name=\"contact\"/></fetch>",
            50, CancellationToken.None);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("\"error\"", json);
        Assert.Contains("entitySetName is required", json);
    }

    [Fact]
    public async Task RunFetchXml_ClientException_SurfacedAsErrorEnvelope()
    {
        _client.Setup(c => c.RunFetchXmlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse 400: malformed FetchXML"));
        var tool = Build();

        var result = await tool.RunFetchXmlAsync(
            "contacts",
            "<fetch><entity name=\"contact\"/></fetch>",
            50,
            CancellationToken.None);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("\"error\"", json);
        Assert.Contains("Dataverse 400: malformed FetchXML", json);
    }
}
