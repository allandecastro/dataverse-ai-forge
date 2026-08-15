using System.ComponentModel;
using System.Text;
using DataverseForge.Application.Contracts;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Tool exposed to DocumentaryAgent.
/// Lists all sources currently registered in the project knowledge base.
/// </summary>
public sealed class ListSourcesTool(IRagStore ragStore, ILogger<ListSourcesTool> logger)
{
    [Description("List all sources currently registered in the project knowledge base, " +
                 "including their category, type, last-ingest date, and source ID. " +
                 "Call this first to know what is available before searching, ingesting, or deleting.")]
    public async Task<string> ListSourcesAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("ListSources called");

        var sources = await ragStore.GetSourcesAsync(cancellationToken);

        if (sources.Count == 0)
            return "The knowledge base is empty — no sources have been ingested yet. " +
                   "Use ingest_documents to add sources.";

        var sb = new StringBuilder();
        sb.AppendLine($"{sources.Count} source(s) in the knowledge base:");
        sb.AppendLine();
        sb.AppendLine($"{"ID",-25} {"Name",-30} {"Category",-18} {"Type",-12} {"Last Ingested"}");
        sb.AppendLine(new string('─', 100));

        foreach (var s in sources)
        {
            var lastIngested = s.LastIngestedAt.HasValue
                ? s.LastIngestedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : "never";
            sb.AppendLine($"{s.Id,-25} {s.Name,-30} {s.Category,-18} {s.SourceType,-12} {lastIngested}");
        }

        return sb.ToString().TrimEnd();
    }
}
