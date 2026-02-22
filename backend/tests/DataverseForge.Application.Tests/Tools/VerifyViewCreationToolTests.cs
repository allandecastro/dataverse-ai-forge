using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class VerifyViewCreationToolTests
{
    private readonly Mock<IDataverseClient> _mockClient = new();

    [Fact]
    public async Task VerifyAsync_ViewExists_ReturnsPass()
    {
        _mockClient
            .Setup(c => c.GetViewsListAsync("cr123_workorder", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ViewInfo>
            {
                new(Guid.NewGuid(), "Active Work Orders", "Shows active WOs", "<fetch/>", "<grid/>")
            });

        var tool = new VerifyViewCreationTool(_mockClient.Object);
        var result = await tool.VerifyViewCreationAsync(
            "cr123_workorder", "Active Work Orders", CancellationToken.None);

        Assert.True(result.Exists);
        Assert.True(result.Matches);
        Assert.Empty(result.Discrepancies);
        Assert.Equal("PASS", result.Verdict);
    }

    [Fact]
    public async Task VerifyAsync_ViewNotFound_ReturnsFail()
    {
        _mockClient
            .Setup(c => c.GetViewsListAsync("cr123_workorder", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ViewInfo>
            {
                new(Guid.NewGuid(), "Other View", "Different view", "<fetch/>", "<grid/>")
            });

        var tool = new VerifyViewCreationTool(_mockClient.Object);
        var result = await tool.VerifyViewCreationAsync(
            "cr123_workorder", "Active Work Orders", CancellationToken.None);

        Assert.False(result.Exists);
        Assert.False(result.Matches);
        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Property == "ViewName");
    }

    [Fact]
    public async Task VerifyAsync_TableNotAccessible_ReturnsFail()
    {
        _mockClient
            .Setup(c => c.GetViewsListAsync("cr123_missing", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Table not found"));

        var tool = new VerifyViewCreationTool(_mockClient.Object);
        var result = await tool.VerifyViewCreationAsync(
            "cr123_missing", "Some View", CancellationToken.None);

        Assert.False(result.Exists);
        Assert.False(result.Matches);
        Assert.Equal("FAIL", result.Verdict);
    }

    [Fact]
    public async Task VerifyAsync_CaseInsensitiveMatch_ReturnsPass()
    {
        _mockClient
            .Setup(c => c.GetViewsListAsync("cr123_workorder", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ViewInfo>
            {
                new(Guid.NewGuid(), "active work orders", "Active WOs", "<fetch/>", "<grid/>")
            });

        var tool = new VerifyViewCreationTool(_mockClient.Object);
        var result = await tool.VerifyViewCreationAsync(
            "cr123_workorder", "Active Work Orders", CancellationToken.None);

        Assert.True(result.Exists);
        Assert.Equal("PASS", result.Verdict);
    }
}
