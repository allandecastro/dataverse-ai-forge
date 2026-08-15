using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using DataverseForge.Application.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ListSourcesToolTests
{
    [Fact]
    public async Task ListSourcesAsync_EmptyKb_ReturnsEmptyMessage()
    {
        var storeMock = new Mock<IRagStore>();
        storeMock.Setup(s => s.GetSourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var tool = new ListSourcesTool(storeMock.Object, NullLogger<ListSourcesTool>.Instance);
        var result = await tool.ListSourcesAsync();

        Assert.Contains("knowledge base is empty", result);
        Assert.Contains("ingest_documents", result);
    }

    [Fact]
    public async Task ListSourcesAsync_WithSources_ShowsCountAndTable()
    {
        var storeMock = new Mock<IRagStore>();
        storeMock.Setup(s => s.GetSourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SourceInfo("fs-abc", "Specs validées", "filesystem", "/specs", SourceCategory.Specification,
                    new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero)),
                new SourceInfo("azdo-backlog", "Backlog AzDo", "azdo", null, SourceCategory.Backlog, null)
            ]);

        var tool = new ListSourcesTool(storeMock.Object, NullLogger<ListSourcesTool>.Instance);
        var result = await tool.ListSourcesAsync();

        Assert.Contains("2 source(s)", result);
        Assert.Contains("fs-abc", result);
        Assert.Contains("Specs validées", result);
        Assert.Contains("Specification", result);
        Assert.Contains("azdo-backlog", result);
        Assert.Contains("Backlog AzDo", result);
        Assert.Contains("never", result); // last ingested = null for backlog
    }

    [Fact]
    public async Task ListSourcesAsync_SourceWithLastIngestedDate_FormatsDate()
    {
        var ingestedAt = new DateTimeOffset(2026, 7, 15, 14, 30, 0, TimeSpan.Zero);
        var storeMock = new Mock<IRagStore>();
        storeMock.Setup(s => s.GetSourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SourceInfo("forge-exports", "Solution Exports", "exports", null, SourceCategory.Implementation, ingestedAt)
            ]);

        var tool = new ListSourcesTool(storeMock.Object, NullLogger<ListSourcesTool>.Instance);
        var result = await tool.ListSourcesAsync();

        Assert.Contains("forge-exports", result);
        Assert.Contains("2026-07-15", result);
    }
}
