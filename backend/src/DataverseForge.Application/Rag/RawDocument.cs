namespace DataverseForge.Application.Rag;

/// <summary>
/// A document fetched from an <see cref="Contracts.IDocumentSource"/> before chunking.
/// </summary>
/// <param name="SourceId">Identifier of the source that produced this document (e.g. "specs", "forge-exports").</param>
/// <param name="SourceName">Human-readable label of the source (e.g. "Specs validées").</param>
/// <param name="FilePath">Logical path or identifier within the source (file path, work-item URL, etc.).</param>
/// <param name="Content">Extracted plain-text content ready for chunking.</param>
/// <param name="ContentType">Hint about the content format used to select chunking strategy: "text", "xml", "vtt".</param>
/// <param name="LastModified">Last-modification time used for incremental-ingest decisions.</param>
public sealed record RawDocument(
    string SourceId,
    string SourceName,
    string FilePath,
    string Content,
    string ContentType,
    DateTimeOffset LastModified);
