using DataverseForge.Application.Rag;

namespace DataverseForge.Application.Contracts;

/// <summary>
/// Persists document chunks with their vector embeddings and provides similarity search.
/// The SQLite + sqlite-vec implementation stores everything in a single local database file
/// at <c>{DVFORGE_DATA_DIR}/rag/rag.db</c>.
/// </summary>
public interface IRagStore
{
    /// <summary>
    /// Register (or update) metadata for a source. Does NOT update <c>last_ingested_at</c> —
    /// call <see cref="MarkIngestedAsync"/> after a successful ingest to record the completion time.
    /// </summary>
    Task UpsertSourceAsync(string sourceId, string sourceName, string sourceType, string? path, SourceCategory category, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record a successful ingest completion for the source. Called at the END of a successful
    /// ingest run so that a failed mid-run does not poison the <c>last_ingested_at</c> timestamp.
    /// </summary>
    Task MarkIngestedAsync(string sourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert or replace chunks for the given document. All existing chunks with the same
    /// <paramref name="sourceId"/> + <paramref name="filePath"/> are deleted first so
    /// re-ingestion of a modified file is idempotent.
    /// </summary>
    Task UpsertChunksAsync(string sourceId, string filePath, DateTimeOffset lastModified, IReadOnlyList<(string Text, ReadOnlyMemory<float> Embedding)> chunks, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all chunks (and the source record) for the given source.
    /// </summary>
    Task DeleteSourceAsync(string sourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Return the last-ingested timestamp for a specific file within a source, or null if not yet ingested.
    /// Used by incremental ingest to skip files that have not changed.
    /// </summary>
    Task<DateTimeOffset?> GetFileLastModifiedAsync(string sourceId, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Return the file paths currently indexed under <paramref name="sourceId"/>.
    /// Used during incremental ingest to detect deleted files.
    /// </summary>
    Task<IReadOnlyList<string>> GetIndexedFilePathsAsync(string sourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Return all stored chunks (with their embeddings) for a given source.
    /// Used by gap-analysis to find uncovered passages without re-embedding.
    /// </summary>
    Task<IReadOnlyList<ChunkInfo>> GetAllChunksAsync(string sourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Similarity search. Optionally restrict results to a specific set of source IDs.
    /// Results are ordered by descending cosine similarity.
    /// </summary>
    Task<IReadOnlyList<RagResult>> SearchAsync(ReadOnlyMemory<float> queryEmbedding, int topK, IReadOnlyList<string>? sourceIds = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Return all registered sources ordered by ascending priority.
    /// </summary>
    Task<IReadOnlyList<SourceInfo>> GetSourcesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Metadata about a registered RAG source.</summary>
public sealed record SourceInfo(string Id, string Name, string SourceType, string? Path, SourceCategory Category, DateTimeOffset? LastIngestedAt);

/// <summary>A stored document chunk with its embedding vector. Used for gap analysis.</summary>
public sealed record ChunkInfo(string SourceId, string FilePath, int ChunkIndex, string Content, ReadOnlyMemory<float> Embedding);
