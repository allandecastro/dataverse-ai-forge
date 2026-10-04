using System.Runtime.CompilerServices;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;

namespace DataverseForge.Infrastructure.Rag;

/// <summary>
/// Reads documents from a local filesystem directory.
/// Supports: .txt, .md, .docx, .pdf, .vtt.
/// Can be instantiated multiple times (one per configured directory).
/// </summary>
public sealed class FilesystemDocumentSource(string id, string name, SourceCategory category, string path) : IDocumentSource
{
    private static readonly string[] SupportedExtensions =
        [".txt", ".md", ".docx", ".pdf", ".vtt", ".pptx", ".xlsx"];

    public string Id { get; } = id;
    public string Name { get; } = name;
    public SourceCategory Category { get; } = category;
    // Filesystem sources enumerate all files; RagService handles incremental via file timestamps.
    public bool SupportsIncrementalFetch => false;

    /// <summary>Filesystem path to the directory to ingest.</summary>
    public string Path { get; } = path;

    public async IAsyncEnumerable<RawDocument> GetDocumentsAsync(
        DateTimeOffset? changedSince = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(Path))
            yield break;

        foreach (var file in Directory.EnumerateFiles(Path, "*.*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
            if (!SupportedExtensions.Contains(ext))
                continue;

            var lastModified = File.GetLastWriteTimeUtc(file);
            string content;

            try
            {
                content = ext switch
                {
                    ".docx" => DocxTextExtractor.Extract(file),
                    ".pdf" => PdfTextExtractor.Extract(file),
                    ".pptx" => PptxTextExtractor.Extract(file),
                    ".xlsx" => XlsxTextExtractor.Extract(file),
                    ".vtt" => VttTextExtractor.Extract(await File.ReadAllTextAsync(file, cancellationToken)),
                    _ => await File.ReadAllTextAsync(file, cancellationToken)
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Skip unreadable files; don't crash the full ingest
                continue;
            }

            if (string.IsNullOrWhiteSpace(content))
                continue;

            var contentType = ext switch
            {
                ".vtt" => "vtt",
                _ => "text"
            };

            yield return new RawDocument(Id, Name, file, content, contentType, new DateTimeOffset(lastModified, TimeSpan.Zero));
        }
    }
}
