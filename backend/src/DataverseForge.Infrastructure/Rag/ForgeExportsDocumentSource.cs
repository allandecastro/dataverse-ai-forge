using System.Runtime.CompilerServices;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using DataverseForge.Infrastructure;

namespace DataverseForge.Infrastructure.Rag;

/// <summary>
/// Reads Dataverse solution XML exports from the forge's data directory.
/// Registered at category <see cref="SourceCategory.Implementation"/> — the ground truth
/// of the current Dataverse configuration.
///
/// Sources: *.xml from DVFORGE_DATA_DIR/exports/ (recursive).
/// Chunking strategy: XPath-aware for Dataverse XML (see <see cref="XmlSolutionTextExtractor"/>).
/// </summary>
public sealed class ForgeExportsDocumentSource : IDocumentSource
{
    public string Id => "forge-exports";
    public string Name => "Solution Exports (forge)";
    public SourceCategory Category => SourceCategory.Implementation;
    // Enumerate all XML files; RagService handles incremental via file timestamps.
    public bool SupportsIncrementalFetch => false;

    public async IAsyncEnumerable<RawDocument> GetDocumentsAsync(
        DateTimeOffset? changedSince = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var exportsDir = ForgePaths.GetSubdir("exports");

        if (!Directory.Exists(exportsDir))
            yield break;

        foreach (var file in Directory.EnumerateFiles(exportsDir, "*.xml", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lastModified = File.GetLastWriteTimeUtc(file);
            string content;

            try
            {
                var xml = await File.ReadAllTextAsync(file, cancellationToken);
                content = XmlSolutionTextExtractor.Extract(xml);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(content))
                continue;

            yield return new RawDocument(Id, Name, file, content, "xml",
                new DateTimeOffset(lastModified, TimeSpan.Zero));
        }
    }
}
