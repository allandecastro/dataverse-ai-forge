namespace DataverseForge.Application.Contracts;

public interface IGitHubClient
{
    Task<string> PushFilesAsync(
        string repositoryOwner,
        string repositoryName,
        string branchName,
        IReadOnlyList<GitHubFileContent> files,
        string commitMessage,
        CancellationToken cancellationToken = default);
}

public record GitHubFileContent(string Path, string Content);
