namespace DataverseForge.Application.Rag;

/// <summary>
/// A single chunk returned by a RAG similarity search, with its provenance and relevance score.
/// </summary>
/// <param name="SourceId">Identifier of the source this chunk came from.</param>
/// <param name="SourceName">Human-readable label of the source (e.g. "Specs validées").</param>
/// <param name="FilePath">Logical path or identifier of the original document within the source.</param>
/// <param name="ChunkIndex">Zero-based index of this chunk within the original document.</param>
/// <param name="Content">Plain-text content of the chunk.</param>
/// <param name="Score">Cosine similarity score in [0, 1]. Higher is more relevant.</param>
public sealed record RagResult(
    string SourceId,
    string SourceName,
    string FilePath,
    int ChunkIndex,
    string Content,
    float Score);
