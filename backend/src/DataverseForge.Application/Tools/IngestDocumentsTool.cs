using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Tool exposed to BusinessAnalystAgent and SolutionArchitectAgent.
/// Registers a document source and triggers incremental ingestion into the RAG store.
/// Supports: exports (forge solution XML), azdo (AzDo backlog), filesystem (local directory),
/// and git (local git repository).
/// </summary>
public sealed class IngestDocumentsTool(
    IRagStore ragStore,
    RagService ragService,
    IEnumerable<IDocumentSource> documentSources,
    IDocumentSourceFactory sourceFactory,
    ILogger<IngestDocumentsTool> logger)
{
    [Description("Ingest documents from a source into the project knowledge base so they can be searched later. " +
                 "Ingest is incremental: only new or modified files are re-processed. " +
                 "Use this when the user provides a path to project documents (specs, transcripts, etc.) " +
                 "or asks to refresh the knowledge base from Azure DevOps.")]
    public async Task<string> IngestDocumentsAsync(
        [Description("Type of source to ingest. 'filesystem' for a local directory, 'exports' for Dataverse solution exports, 'azdo' for Azure DevOps backlog, 'git' for a local Git repository.")] string sourceType,
        [Description("Human-readable label for this source (e.g. 'Specs validées', 'Transcripts réunions'). Used in search results.")] string label,
        [Description("Epistemic category of this source. Controls search priority based on query intent. " +
                     "Values: 'Implementation' (what IS deployed — forge exports), " +
                     "'Specification' (what SHOULD be — validated specs, functional docs), " +
                     "'ClientProvided' (client's stated intent — documents sent by the client), " +
                     "'Transcript' (what was said — meeting recordings), " +
                     "'Backlog' (what is planned — AzDo work items), " +
                     "'Internal' (internal notes, architecture docs, misc).")] string category = "Internal",
        [Description("Filesystem path to the directory to ingest. Required when sourceType is 'filesystem' or 'git'.")] string? path = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("IngestDocuments — type={SourceType}, label={Label}, category={Category}, path={Path}",
            sourceType, label, category, path);

        var parsedCategory = Enum.TryParse<SourceCategory>(category, ignoreCase: true, out var cat)
            ? cat
            : SourceCategory.Internal;

        var normalised = sourceType.Trim().ToLowerInvariant();

        // Derive a stable source ID from the type and path
        var sourceId = normalised switch
        {
            "exports"    => "forge-exports",
            "azdo"       => "azdo-backlog",
            "filesystem" when !string.IsNullOrWhiteSpace(path) =>
                "fs-" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
                    System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..8],
            "git" when !string.IsNullOrWhiteSpace(path) =>
                "git-" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
                    System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..8],
            _ => null
        };

        if (sourceId is null)
            return "Error: sourceType must be 'filesystem' or 'git' (with a path), 'exports', or 'azdo'.";

        // ── Dynamic sources: create on demand, no DI registration needed ─────────────
        if (normalised is "filesystem" && !string.IsNullOrWhiteSpace(path))
        {
            if (!Directory.Exists(path))
                return $"Error: directory '{path}' does not exist or is not accessible.";
            var dynamicSource = sourceFactory.CreateFilesystem(sourceId, label, parsedCategory, path);
            return await IngestAndReport(dynamicSource, label, cancellationToken);
        }

        if (normalised is "git" && !string.IsNullOrWhiteSpace(path))
        {
            if (!Directory.Exists(path))
                return $"Error: git repository path '{path}' does not exist or is not accessible.";
            var dynamicSource = sourceFactory.CreateGitRepository(sourceId, label, parsedCategory, path);
            return await IngestAndReport(dynamicSource, label, cancellationToken);
        }

        // ── DI-registered sources (exports, azdo) ────────────────────────────────────
        var source = documentSources.FirstOrDefault(s => s.Id == sourceId);

        if (source is null)
        {
            // Source should be registered in DI but isn't — surface a useful diagnosis.
            var hint = sourceId == "azdo-backlog"
                ? "Azure DevOps credentials (AzureDevOps:OrganizationUrl, AzureDevOps:Project, AzureDevOps:PersonalAccessToken) may be missing or incomplete in Settings."
                : $"Source '{sourceId}' is not registered. Check application configuration.";
            return $"Error: {hint}";
        }

        // Persist any label/category update, then ingest
        await ragStore.UpsertSourceAsync(source.Id, label, normalised, null, parsedCategory, cancellationToken);
        return await IngestAndReport(source, label, cancellationToken);
    }

    private async Task<string> IngestAndReport(IDocumentSource source, string label, CancellationToken ct)
    {
        var summary = await ragService.IngestSourceAsync(source, ct);

        if (summary.ErrorMessage is not null)
            return $"Ingestion error: {summary.ErrorMessage}";

        return $"Ingestion complete for '{label}': " +
               $"{summary.DocumentsIngested} document(s) ingested, " +
               $"{summary.ChunksIngested} chunks indexed, " +
               $"{summary.DocumentsSkipped} unchanged (skipped).";
    }
}
