using DataverseForge.Application.Rag;

namespace DataverseForge.Application.Contracts;

/// <summary>
/// Factory that creates <see cref="IDocumentSource"/> instances for sources supplied at
/// runtime (filesystem directories and git repositories). Defined in the Application layer
/// so <see cref="Tools.IngestDocumentsTool"/> can create dynamic sources without taking a
/// direct dependency on Infrastructure concrete types.
/// </summary>
public interface IDocumentSourceFactory
{
    /// <summary>Create a filesystem source that reads documents from <paramref name="path"/>.</summary>
    IDocumentSource CreateFilesystem(string id, string name, SourceCategory category, string path);

    /// <summary>Create a Git repository source rooted at <paramref name="localPath"/>.</summary>
    IDocumentSource CreateGitRepository(string id, string name, SourceCategory category, string localPath);

    /// <summary>The project's Azure DevOps backlog, with the project's own credentials.</summary>
    IDocumentSource CreateAzureDevOps(AzureDevOpsConnection connection);
}
