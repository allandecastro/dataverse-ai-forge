using System.ComponentModel;
using DataverseForge.Application.Contracts;

namespace DataverseForge.Application.Tools;

public class PushDocumentationToGitHubTool(IGitHubClient gitHubClient)
{
    [Description("Push documentation files to a GitHub repository. "
        + "Creates or updates files on the specified branch with a commit message.")]
    public async Task<string> PushDocumentationToGitHubAsync(
        [Description("GitHub repository owner (user or organization)")] string repositoryOwner,
        [Description("GitHub repository name")] string repositoryName,
        [Description("Branch name to push to (e.g. 'main' or 'docs/update')")] string branchName,
        [Description("Comma-separated list of file paths to push (e.g. 'docs/data-dictionary.md,docs/changelog.md')")] string filePaths,
        [Description("Comma-separated list of file contents matching the file paths")] string fileContents,
        [Description("Commit message for the documentation push")] string commitMessage,
        CancellationToken cancellationToken)
    {
        var paths = filePaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var contents = fileContents.Split(new[] { "|||" }, StringSplitOptions.None);

        if (paths.Length == 0)
        {
            return "No file paths provided. Specify at least one file to push.";
        }

        if (paths.Length != contents.Length)
        {
            return $"Mismatch: {paths.Length} file paths but {contents.Length} content blocks. Use '|||' to separate content blocks.";
        }

        var files = new List<GitHubFileContent>();
        for (var i = 0; i < paths.Length; i++)
        {
            files.Add(new GitHubFileContent(paths[i], contents[i]));
        }

        var commitUrl = await gitHubClient.PushFilesAsync(
            repositoryOwner, repositoryName, branchName, files, commitMessage, cancellationToken);

        return $"Documentation pushed successfully. Commit: {commitUrl}";
    }
}
