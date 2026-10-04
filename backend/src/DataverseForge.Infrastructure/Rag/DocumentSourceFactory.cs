using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Rag;

/// <summary>
/// Infrastructure implementation of <see cref="IDocumentSourceFactory"/>.
/// Creates <see cref="FilesystemDocumentSource"/> and <see cref="GitRepositoryDocumentSource"/>
/// on demand for paths supplied at runtime by the user (via <c>ingest_documents</c> tool).
/// </summary>
public sealed class DocumentSourceFactory(ILoggerFactory loggerFactory) : IDocumentSourceFactory
{
    public IDocumentSource CreateFilesystem(string id, string name, SourceCategory category, string path)
        => new FilesystemDocumentSource(id, name, category, path);

    public IDocumentSource CreateGitRepository(string id, string name, SourceCategory category, string localPath)
        => new GitRepositoryDocumentSource(id, name, category, localPath,
            loggerFactory.CreateLogger<GitRepositoryDocumentSource>());

    public IDocumentSource CreateAzureDevOps(AzureDevOpsConnection connection)
        => new AzureDevOpsDocumentSource(connection.OrganizationUrl, connection.Project, connection.PersonalAccessToken,
            loggerFactory.CreateLogger<AzureDevOpsDocumentSource>());
}
