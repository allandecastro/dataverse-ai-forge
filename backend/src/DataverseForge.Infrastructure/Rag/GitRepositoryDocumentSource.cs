using System.Diagnostics;
using System.Runtime.CompilerServices;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Rag;

/// <summary>
/// Reads text documents from a local Git repository.
///
/// <para><b>Full ingest</b> (first run): lists all tracked files via <c>git ls-files</c>
/// and reads those with supported extensions.</para>
///
/// <para><b>Incremental ingest</b>: uses <c>git log --after="{date}" --diff-filter=ACMR --name-only</c>
/// to retrieve only files added, copied, modified, or renamed since <paramref name="changedSince"/>.
/// Deleted files are handled by the file-not-found guard (their chunks remain but will not be re-embedded;
/// call <c>ingest_documents</c> without a previous ingest date to trigger a full prune).</para>
///
/// <para>Before enumeration, <c>git pull --ff-only --quiet</c> is attempted. Failure (e.g. offline,
/// no remote) is logged as a warning and enumeration proceeds from the local state.</para>
/// </summary>
public sealed class GitRepositoryDocumentSource(
    string id,
    string name,
    SourceCategory category,
    string localPath,
    ILogger<GitRepositoryDocumentSource> logger) : IDocumentSource
{
    private static readonly string[] SupportedExtensions =
        [".txt", ".md", ".cs", ".json", ".xml", ".yaml", ".yml",
         ".ts", ".tsx", ".js", ".jsx",
         ".docx", ".pdf", ".pptx", ".xlsx", ".vtt"];

    public string Id { get; } = id;
    public string Name { get; } = name;
    public SourceCategory Category { get; } = category;
    public bool SupportsIncrementalFetch => true;

    /// <summary>Local filesystem path to the root of the git repository.</summary>
    public string LocalPath { get; } = localPath;

    public async IAsyncEnumerable<RawDocument> GetDocumentsAsync(
        DateTimeOffset? changedSince = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(LocalPath))
        {
            logger.LogWarning("Git repository path '{Path}' does not exist", LocalPath);
            yield break;
        }

        // Pull latest changes; non-fatal on failure
        await TryGitPullAsync(cancellationToken);

        var relativePaths = changedSince.HasValue
            ? await GetChangedFilePathsAsync(changedSince.Value, cancellationToken)
            : await GetAllTrackedFilePathsAsync(cancellationToken);

        foreach (var relativePath in relativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ext = Path.GetExtension(relativePath).ToLowerInvariant();
            if (!SupportedExtensions.Contains(ext))
                continue;

            var fullPath = Path.Combine(LocalPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
                continue; // File was deleted after the git log query; skip.

            var lastModified = new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath), TimeSpan.Zero);
            string content;

            try
            {
                content = ext switch
                {
                    ".docx" => DocxTextExtractor.Extract(fullPath),
                    ".pdf" => PdfTextExtractor.Extract(fullPath),
                    ".pptx" => PptxTextExtractor.Extract(fullPath),
                    ".xlsx" => XlsxTextExtractor.Extract(fullPath),
                    ".vtt" => VttTextExtractor.Extract(await File.ReadAllTextAsync(fullPath, cancellationToken)),
                    _ => await File.ReadAllTextAsync(fullPath, cancellationToken)
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not read git file '{Path}'", fullPath);
                continue;
            }

            if (string.IsNullOrWhiteSpace(content))
                continue;

            // Normalise path to forward-slash, repo-root-relative for stable IDs across OSes
            var normalizedPath = $"git://{Id}/{relativePath.Replace(Path.DirectorySeparatorChar, '/')}";
            yield return new RawDocument(Id, Name, normalizedPath, content, "text", lastModified);
        }
    }

    // ─── Git helpers ─────────────────────────────────────────────────────────

    private async Task TryGitPullAsync(CancellationToken ct)
    {
        try
        {
            var result = await RunGitAsync("pull --ff-only --quiet", ct);
            if (result.ExitCode != 0)
                logger.LogWarning("git pull returned {ExitCode}: {Error}", result.ExitCode, result.StdErr);
            else
                logger.LogDebug("git pull completed for '{Path}'", LocalPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "git pull failed for '{Path}' — ingesting from local state", LocalPath);
        }
    }

    private async Task<IReadOnlyList<string>> GetAllTrackedFilePathsAsync(CancellationToken ct)
    {
        var result = await RunGitAsync("ls-files", ct);
        if (result.ExitCode != 0)
        {
            logger.LogWarning("git ls-files failed: {Error}", result.StdErr);
            return [];
        }
        return result.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private async Task<IReadOnlyList<string>> GetChangedFilePathsAsync(DateTimeOffset changedSince, CancellationToken ct)
    {
        // --diff-filter=ACMR: Added, Copied, Modified, Renamed (excludes Deleted)
        var since = changedSince.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var result = await RunGitAsync(
            $"log --after=\"{since}\" --diff-filter=ACMR --name-only --pretty=format:", ct);

        if (result.ExitCode != 0)
        {
            logger.LogWarning("git log failed: {Error}", result.StdErr);
            return [];
        }

        return result.StdOut
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunGitAsync(string arguments, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"-C \"{LocalPath}\" {arguments}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();
        var stdOut = await process.StandardOutput.ReadToEndAsync(ct);
        var stdErr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return (process.ExitCode, stdOut, stdErr);
    }
}
