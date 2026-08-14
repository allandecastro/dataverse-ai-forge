using System.ComponentModel;
using DataverseForge.Application.Contracts;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Tool exposed to DocumentaryAgent.
/// Removes a source (and all its indexed chunks) from the project knowledge base.
/// </summary>
public sealed class DeleteSourceTool(IRagStore ragStore, ILogger<DeleteSourceTool> logger)
{
    [Description("Remove a source and all its indexed content from the project knowledge base. " +
                 "Use when a source is outdated, replaced by a newer version, or no longer relevant. " +
                 "Call list_sources first to confirm the source ID before deleting.")]
    public async Task<string> DeleteSourceAsync(
        [Description("The source ID to delete (as returned by list_sources).")] string sourceId,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("DeleteSource called for '{SourceId}'", sourceId);

        var sources = await ragStore.GetSourcesAsync(cancellationToken);
        var existing = sources.FirstOrDefault(s => s.Id == sourceId);

        if (existing is null)
            return $"Source '{sourceId}' not found in the knowledge base. " +
                   "Use list_sources to see available source IDs.";

        await ragStore.DeleteSourceAsync(sourceId, cancellationToken);

        logger.LogInformation("Source '{SourceId}' ({Name}) deleted from knowledge base", sourceId, existing.Name);
        return $"Source '{sourceId}' ({existing.Name}, category: {existing.Category}) has been removed from the knowledge base.";
    }
}
