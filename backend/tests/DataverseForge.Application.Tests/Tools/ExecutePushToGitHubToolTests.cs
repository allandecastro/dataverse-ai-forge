using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecutePushToGitHubToolTests
{
    private readonly Mock<IGitHubClient> _mockGitHub = new();

    [Fact]
    public async Task ExecutePushToGitHub_ValidFiles_ReturnsSuccessMessage()
    {
        _mockGitHub
            .Setup(g => g.PushFilesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<GitHubFileContent>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("abc123");

        var tool = new ExecutePushToGitHubTool(_mockGitHub.Object);
        var result = await tool.ExecutePushToGitHubAsync(
            "owner", "repo", "feature/plugin",
            "src/Plugin.cs",
            "// plugin code",
            "feat: add plugin",
            false, null, null,
            CancellationToken.None);

        Assert.Contains("feature/plugin", result);
        Assert.Contains("abc123", result);
    }

    [Fact]
    public async Task ExecutePushToGitHub_NoFilePaths_ReturnsError()
    {
        var tool = new ExecutePushToGitHubTool(_mockGitHub.Object);
        var result = await tool.ExecutePushToGitHubAsync(
            "owner", "repo", "feature/plugin",
            "",
            "",
            "feat: empty push",
            false, null, null,
            CancellationToken.None);

        Assert.Contains("No file paths provided", result);
    }

    [Fact]
    public async Task ExecutePushToGitHub_MismatchedPathsAndContents_ReturnsError()
    {
        var tool = new ExecutePushToGitHubTool(_mockGitHub.Object);
        var result = await tool.ExecutePushToGitHubAsync(
            "owner", "repo", "feature/plugin",
            "src/File1.cs,src/File2.cs",
            "only one content block",
            "feat: mismatch",
            false, null, null,
            CancellationToken.None);

        Assert.Contains("Mismatch", result);
    }

    [Fact]
    public async Task ExecutePushToGitHub_MultipleFiles_PassesAllToClient()
    {
        IReadOnlyList<GitHubFileContent>? capturedFiles = null;
        _mockGitHub
            .Setup(g => g.PushFilesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<GitHubFileContent>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, IReadOnlyList<GitHubFileContent>, string, CancellationToken>(
                (_, _, _, files, _, _) => capturedFiles = files)
            .ReturnsAsync("def456");

        var tool = new ExecutePushToGitHubTool(_mockGitHub.Object);
        await tool.ExecutePushToGitHubAsync(
            "owner", "repo", "feature/plugin",
            "src/Plugin.cs,src/Plugin.csproj,src/README.md",
            "// plugin|||<Project />|||# Readme",
            "feat: add plugin files",
            false, null, null,
            CancellationToken.None);

        Assert.NotNull(capturedFiles);
        Assert.Equal(3, capturedFiles!.Count);
        Assert.Equal("src/Plugin.cs", capturedFiles[0].Path);
        Assert.Equal("// plugin", capturedFiles[0].Content);
        Assert.Equal("src/Plugin.csproj", capturedFiles[1].Path);
        Assert.Equal("<Project />", capturedFiles[1].Content);
        Assert.Equal("src/README.md", capturedFiles[2].Path);
        Assert.Equal("# Readme", capturedFiles[2].Content);
    }

    [Fact]
    public async Task ExecutePushToGitHub_CreatePullRequest_AppendsPrMessage()
    {
        _mockGitHub
            .Setup(g => g.PushFilesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<GitHubFileContent>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("abc123");

        var tool = new ExecutePushToGitHubTool(_mockGitHub.Object);
        var result = await tool.ExecutePushToGitHubAsync(
            "owner", "repo", "feature/plugin",
            "src/Plugin.cs",
            "// plugin code",
            "feat: add plugin",
            true, "Add validation plugin", "Adds work order validation",
            CancellationToken.None);

        Assert.Contains("feature/plugin", result);
        Assert.Contains("abc123", result);
        Assert.Contains("Pull request", result);
        Assert.Contains("Add validation plugin", result);
    }
}
