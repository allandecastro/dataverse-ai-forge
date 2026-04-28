using DataverseForge.Application.Contracts;
using Microsoft.Extensions.Logging;
using Octokit;

namespace DataverseForge.Infrastructure.GitHub;

internal sealed class OctokitGitHubClient : Application.Contracts.IGitHubClient
{
    private readonly GitHubClient _client;
    private readonly ILogger<OctokitGitHubClient> _logger;
    private readonly bool _hasToken;

    public OctokitGitHubClient(string? token, ILogger<OctokitGitHubClient> logger)
    {
        _logger = logger;
        _client = new GitHubClient(new ProductHeaderValue("DataverseAIForge"));
        _hasToken = !string.IsNullOrWhiteSpace(token);
        if (_hasToken)
        {
            _client.Credentials = new Credentials(token);
        }
    }

    public async Task<string> PushFilesAsync(
        string repositoryOwner,
        string repositoryName,
        string branchName,
        IReadOnlyList<GitHubFileContent> files,
        string commitMessage,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Pushing {Count} files to {Owner}/{Repo}@{Branch}",
            files.Count, repositoryOwner, repositoryName, branchName);

        var reference = await _client.Git.Reference.Get(repositoryOwner, repositoryName, $"heads/{branchName}");
        var latestCommit = await _client.Git.Commit.Get(repositoryOwner, repositoryName, reference.Object.Sha);
        var baseTree = latestCommit.Tree;

        var newTreeItems = new List<NewTreeItem>();
        foreach (var file in files)
        {
            var blob = new NewBlob { Content = file.Content, Encoding = EncodingType.Utf8 };
            var blobResult = await _client.Git.Blob.Create(repositoryOwner, repositoryName, blob);
            newTreeItems.Add(new NewTreeItem
            {
                Path = file.Path,
                Mode = "100644",
                Type = TreeType.Blob,
                Sha = blobResult.Sha
            });
        }

        var newTree = new NewTree { BaseTree = baseTree.Sha };
        foreach (var item in newTreeItems)
        {
            newTree.Tree.Add(item);
        }
        var treeResult = await _client.Git.Tree.Create(repositoryOwner, repositoryName, newTree);

        var newCommit = new NewCommit(commitMessage, treeResult.Sha, reference.Object.Sha);
        var commitResult = await _client.Git.Commit.Create(repositoryOwner, repositoryName, newCommit);

        await _client.Git.Reference.Update(repositoryOwner, repositoryName, $"heads/{branchName}",
            new ReferenceUpdate(commitResult.Sha));

        _logger.LogInformation("Successfully pushed commit {Sha}", commitResult.Sha);
        return commitResult.Sha;
    }

    public async Task<bool> CheckConnectivityAsync(CancellationToken cancellationToken = default)
    {
        // Require an authenticated check — no token means not configured
        if (!_hasToken) return false;
        try
        {
            // Authenticated user lookup; fails if token is invalid
            await _client.User.Current();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GitHub connectivity check failed");
            return false;
        }
    }
}
