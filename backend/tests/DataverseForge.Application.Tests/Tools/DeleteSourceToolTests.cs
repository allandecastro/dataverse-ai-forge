using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using DataverseForge.Application.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class DeleteSourceToolTests
{
    [Fact]
    public async Task DeleteSourceAsync_SourceNotFound_ReturnsNotFoundMessage()
    {
        var storeMock = new Mock<IRagStore>();
        storeMock.Setup(s => s.GetSourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var tool = new DeleteSourceTool(storeMock.Object, NullLogger<DeleteSourceTool>.Instance);
        var result = await tool.DeleteSourceAsync("non-existent-source");

        Assert.Contains("not found", result);
        Assert.Contains("list_sources", result);
        storeMock.Verify(s => s.DeleteSourceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteSourceAsync_ExistingSource_CallsStoreDelete()
    {
        var storeMock = new Mock<IRagStore>();
        storeMock.Setup(s => s.GetSourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SourceInfo("fs-abc", "Old Specs", "filesystem", "/old", SourceCategory.Specification, null)
            ]);
        storeMock.Setup(s => s.DeleteSourceAsync("fs-abc", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tool = new DeleteSourceTool(storeMock.Object, NullLogger<DeleteSourceTool>.Instance);
        var result = await tool.DeleteSourceAsync("fs-abc");

        storeMock.Verify(s => s.DeleteSourceAsync("fs-abc", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("fs-abc", result);
        Assert.Contains("removed", result);
    }

    [Fact]
    public async Task DeleteSourceAsync_ExistingSource_IncludesNameAndCategoryInConfirmation()
    {
        var storeMock = new Mock<IRagStore>();
        storeMock.Setup(s => s.GetSourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SourceInfo("azdo-backlog", "Backlog AzDo", "azdo", null, SourceCategory.Backlog, null)
            ]);
        storeMock.Setup(s => s.DeleteSourceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tool = new DeleteSourceTool(storeMock.Object, NullLogger<DeleteSourceTool>.Instance);
        var result = await tool.DeleteSourceAsync("azdo-backlog");

        Assert.Contains("Backlog AzDo", result);
        Assert.Contains("Backlog", result);
    }
}
