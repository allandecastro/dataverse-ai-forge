using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PushDocumentationToGitHubToolTests
{
    private readonly Mock<IGitHubClient> _mockGitHub = new();

    [Fact]
    public async Task PushAsync_ValidFiles_ReturnsSuccessMessage()
    {
        _mockGitHub
            .Setup(g => g.PushFilesAsync(
                "owner", "repo", "main",
                It.IsAny<IReadOnlyList<GitHubFileContent>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("abc123");

        var tool = new PushDocumentationToGitHubTool(_mockGitHub.Object);
        var result = await tool.PushDocumentationToGitHubAsync(
            "owner", "repo", "main",
            "docs/data-dictionary.md",
            "# Data Dictionary content",
            "docs: update data dictionary",
            CancellationToken.None);

        Assert.Contains("successfully", result);
        Assert.Contains("abc123", result);
    }

    [Fact]
    public async Task PushAsync_NoFilePaths_ReturnsError()
    {
        var tool = new PushDocumentationToGitHubTool(_mockGitHub.Object);
        var result = await tool.PushDocumentationToGitHubAsync(
            "owner", "repo", "main",
            "",
            "",
            "docs: empty push",
            CancellationToken.None);

        Assert.Contains("No file paths", result);
    }

    [Fact]
    public async Task PushAsync_MismatchedPathsAndContents_ReturnsError()
    {
        var tool = new PushDocumentationToGitHubTool(_mockGitHub.Object);
        var result = await tool.PushDocumentationToGitHubAsync(
            "owner", "repo", "main",
            "docs/file1.md,docs/file2.md",
            "only one content block",
            "docs: mismatch",
            CancellationToken.None);

        Assert.Contains("Mismatch", result);
    }

    [Fact]
    public async Task PushAsync_MultipleFiles_PassesAllToClient()
    {
        IReadOnlyList<GitHubFileContent>? capturedFiles = null;
        _mockGitHub
            .Setup(g => g.PushFilesAsync(
                "owner", "repo", "main",
                It.IsAny<IReadOnlyList<GitHubFileContent>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, IReadOnlyList<GitHubFileContent>, string, CancellationToken>(
                (_, _, _, files, _, _) => capturedFiles = files)
            .ReturnsAsync("def456");

        var tool = new PushDocumentationToGitHubTool(_mockGitHub.Object);
        await tool.PushDocumentationToGitHubAsync(
            "owner", "repo", "main",
            "docs/dd.md,docs/cl.md",
            "DD content|||CL content",
            "docs: update all",
            CancellationToken.None);

        Assert.NotNull(capturedFiles);
        Assert.Equal(2, capturedFiles!.Count);
        Assert.Equal("docs/dd.md", capturedFiles[0].Path);
        Assert.Equal("DD content", capturedFiles[0].Content);
        Assert.Equal("docs/cl.md", capturedFiles[1].Path);
        Assert.Equal("CL content", capturedFiles[1].Content);
    }
}
