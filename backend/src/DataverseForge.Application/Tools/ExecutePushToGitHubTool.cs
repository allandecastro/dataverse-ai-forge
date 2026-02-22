using System.ComponentModel;
using DataverseForge.Application.Contracts;

namespace DataverseForge.Application.Tools;

public class ExecutePushToGitHubTool(IGitHubClient gitHubClient)
{
    [Description("Push plugin code and project files to a GitHub repository. Creates a feature branch, "
        + "commits files, and optionally creates a pull request.")]
    public async Task<string> ExecutePushToGitHubAsync(
        [Description("GitHub repository owner (user or organization)")] string repositoryOwner,
        [Description("GitHub repository name")] string repositoryName,
        [Description("Branch name (e.g., 'feature/workorder-validation-plugin')")] string branchName,
        [Description("Comma-separated list of file paths")] string filePaths,
        [Description("File contents separated by '|||' delimiter, matching the file paths order")] string fileContents,
        [Description("Commit message")] string commitMessage,
        [Description("Whether to create a pull request")] bool createPullRequest,
        [Description("Pull request title (required if createPullRequest is true)")] string? prTitle,
        [Description("Pull request description (optional)")] string? prDescription,
        CancellationToken cancellationToken)
    {
        var paths = filePaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var contents = fileContents.Split(["|||"], StringSplitOptions.None);

        if (paths.Length == 0)
            return "No file paths provided. Specify at least one file to push.";

        if (paths.Length != contents.Length)
            return $"Mismatch: {paths.Length} file paths but {contents.Length} content blocks. Use '|||' to separate content blocks.";

        var files = new List<GitHubFileContent>();
        for (var i = 0; i < paths.Length; i++)
        {
            files.Add(new GitHubFileContent(paths[i], contents[i]));
        }

        var commitUrl = await gitHubClient.PushFilesAsync(
            repositoryOwner, repositoryName, branchName, files, commitMessage, cancellationToken);

        var result = $"Code pushed to branch '{branchName}'. Commit: {commitUrl}";

        if (createPullRequest && !string.IsNullOrWhiteSpace(prTitle))
        {
            result += $"\nPull request creation requested: '{prTitle}' — PR creation requires manual step or GitHub Actions workflow.";
        }

        return result;
    }
}
