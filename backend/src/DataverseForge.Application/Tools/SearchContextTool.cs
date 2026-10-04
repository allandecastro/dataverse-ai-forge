using System.ComponentModel;
using DataverseForge.Application.Rag;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Tool exposed to BusinessAnalystAgent and SolutionArchitectAgent.
/// Performs a cascading similarity search over all ingested project documents.
///
/// The agent calls this automatically when it needs project context (e.g. before
/// generating a user story, a spec, or a data-model recommendation).
/// </summary>
public sealed class SearchContextTool(RagService ragService, ILogger<SearchContextTool> logger)
{
    [Description("Search the project knowledge base for information relevant to the given query. " +
                 "Use this before generating user stories, specs, or technical decisions to ground " +
                 "your answer in the actual project documentation. " +
                 "Returns the most relevant passages with their source and relevance score.")]
    public async Task<string> SearchContextAsync(
        [Description("Natural-language query describing the information you are looking for.")] string query,
        [Description("Search intent: controls which source categories are searched first. " +
                     "'implemented-behavior' = what IS deployed (forge exports first); " +
                     "'expected-behavior' = what SHOULD be / bug analysis (specs first); " +
                     "'planning' = what is planned (backlog first); " +
                     "'context' = project context / client intent (client-provided + transcripts first). " +
                     "Omit for a general search across all sources simultaneously.")] string? intent = null,
        [Description("Maximum number of passages to return (default: 5).")] int topK = 5,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("SearchContext called with query='{Query}', intent='{Intent}', topK={TopK}", query, intent, topK);

        var results = await ragService.SearchAsync(query, topK, intent, cancellationToken);

        if (results.Count == 0)
            return "No relevant context found in the project knowledge base. Consider ingesting project documents first using the ingest_documents tool.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Found {results.Count} relevant passage(s):");
        sb.AppendLine();

        foreach (var (result, index) in results.Select((r, i) => (r, i + 1)))
        {
            sb.AppendLine($"[{index}] Source: {result.SourceName} — {result.FilePath} (score: {result.Score.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)})");
            sb.AppendLine(result.Content);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
