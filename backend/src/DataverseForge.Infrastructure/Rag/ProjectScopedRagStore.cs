using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;

namespace DataverseForge.Infrastructure.Rag;

/// <summary>
/// Scoped <see cref="IRagStore"/> that routes every call to the current project's own
/// database (see <see cref="RagStoreProvider"/>). The session id is read on every call rather
/// than captured at construction, so it always reflects the session the orchestrator set on
/// <see cref="ISessionContext"/> for the current turn.
/// </summary>
public sealed class ProjectScopedRagStore(RagStoreProvider provider, ISessionContext sessionContext) : IRagStore
{
    private SqliteRagStore Current => provider.GetStore(sessionContext.SessionId);

    public Task UpsertSourceAsync(string sourceId, string sourceName, string sourceType, string? path, SourceCategory category, CancellationToken cancellationToken = default) =>
        Current.UpsertSourceAsync(sourceId, sourceName, sourceType, path, category, cancellationToken);

    public Task MarkIngestedAsync(string sourceId, CancellationToken cancellationToken = default) =>
        Current.MarkIngestedAsync(sourceId, cancellationToken);

    public Task UpsertChunksAsync(string sourceId, string filePath, DateTimeOffset lastModified, IReadOnlyList<(string Text, ReadOnlyMemory<float> Embedding)> chunks, CancellationToken cancellationToken = default) =>
        Current.UpsertChunksAsync(sourceId, filePath, lastModified, chunks, cancellationToken);

    public Task DeleteSourceAsync(string sourceId, CancellationToken cancellationToken = default) =>
        Current.DeleteSourceAsync(sourceId, cancellationToken);

    public Task<DateTimeOffset?> GetFileLastModifiedAsync(string sourceId, string filePath, CancellationToken cancellationToken = default) =>
        Current.GetFileLastModifiedAsync(sourceId, filePath, cancellationToken);

    public Task<IReadOnlyList<string>> GetIndexedFilePathsAsync(string sourceId, CancellationToken cancellationToken = default) =>
        Current.GetIndexedFilePathsAsync(sourceId, cancellationToken);

    public Task<IReadOnlyList<ChunkInfo>> GetAllChunksAsync(string sourceId, CancellationToken cancellationToken = default) =>
        Current.GetAllChunksAsync(sourceId, cancellationToken);

    public Task<IReadOnlyList<RagResult>> SearchAsync(ReadOnlyMemory<float> queryEmbedding, int topK, IReadOnlyList<string>? sourceIds = null, CancellationToken cancellationToken = default) =>
        Current.SearchAsync(queryEmbedding, topK, sourceIds, cancellationToken);

    public Task<IReadOnlyList<SourceInfo>> GetSourcesAsync(CancellationToken cancellationToken = default) =>
        Current.GetSourcesAsync(cancellationToken);
}
