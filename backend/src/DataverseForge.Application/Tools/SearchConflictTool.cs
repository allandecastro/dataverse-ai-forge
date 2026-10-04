using System.ComponentModel;
using System.Text;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Tool exposed to DocumentaryAgent.
/// Detects confirmed contradictions between two ingested sources, statement by statement:
/// <list type="bullet">
///   <item>The LLM extracts discrete statements from source A.</item>
///   <item>Each statement is searched against source B via RAG (top matches, no similarity cap).</item>
///   <item>The LLM decides whether B contradicts, complements, or is unrelated to the statement.</item>
/// </list>
/// Same shape as <see cref="CompareSourcesTool"/>. It replaced a cosine-band pre-filter
/// ([0.45, 0.72]) that missed the most common real conflict: two near-identical sentences
/// that differ on one value ("above 500 EUR" vs "above 1000 EUR") score well above the band.
/// </summary>
public sealed class SearchConflictTool(
    RagService ragService,
    IRagStore ragStore,
    ILlmAnalysisService llmAnalysis,
    ILogger<SearchConflictTool> logger)
{
    private const int MatchesPerStatement = 3;

    /// <summary>
    /// Upper bound on statements checked per run (one RAG search + one LLM call each),
    /// matching <see cref="CompareSourcesTool"/>'s cap.
    /// </summary>
    internal const int MaxStatements = 100;

    [Description("Find genuine contradictions between two ingested sources. " +
                 "Extracts each statement from the first source, finds the matching passages in the second, " +
                 "and uses LLM reasoning to confirm real conflicts (different thresholds, durations, " +
                 "mandatory vs optional, etc.). " +
                 "Useful for detecting where the implementation diverges from the spec, " +
                 "or where two spec versions disagree. " +
                 "Both sources must be ingested before calling. " +
                 "Returns only confirmed contradictions with explanations.")]
    public async Task<string> SearchConflictAsync(
        [Description("Source ID of the first document to compare (e.g. specs).")] string sourceAId,
        [Description("Source ID of the second document to compare against (e.g. exports or backlog).")] string sourceBId,
        [Description("Optional topic or area to focus on (e.g. invoice validation rules). Omit to scan all content.")] string? query = null,
        [Description("When a query is given, how many of source A's most relevant passages to analyse (default 20).")] int topK = 20,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("SearchConflict called: A='{SourceA}' vs B='{SourceB}' query='{Query}'",
            sourceAId, sourceBId, query ?? "(all)");

        var passagesByFile = await GetSourceAPassagesAsync(sourceAId, query, topK, cancellationToken);
        if (passagesByFile.Count == 0)
        {
            var scope = query is null ? "" : $" related to '{query}'";
            return $"No indexed content{scope} found for source '{sourceAId}'. " +
                   "Ingest it first using the ingest_documents tool, then retry.";
        }

        // Pass 1: discrete statements from A (capped — each one costs a search + an LLM call).
        var statements = new List<(string File, string Statement)>();
        var truncated = false;
        foreach (var (file, passages) in passagesByFile)
        {
            if (statements.Count >= MaxStatements)
            {
                truncated = true;
                break;
            }

            var extracted = await llmAnalysis.ExtractRequirementsAsync(string.Join("\n\n", passages), cancellationToken);
            var room = MaxStatements - statements.Count;
            if (extracted.Count > room)
                truncated = true;
            statements.AddRange(extracted.Take(room).Select(s => (file, s)));
        }

        if (statements.Count == 0)
            return $"No discrete statements could be extracted from '{sourceAId}'.";

        // Pass 2: for each statement, what does B say about the same thing?
        var bIds = new[] { sourceBId };
        var confirmed = new List<(string File, string Statement, RagResult Match, ConflictVerdict Verdict)>();
        var noMatch = 0;

        foreach (var (file, statement) in statements)
        {
            var matches = await ragService.SearchBySourcesAsync(statement, MatchesPerStatement, bIds, cancellationToken);
            if (matches.Count == 0)
            {
                noMatch++;
                continue;
            }

            var passageB = string.Join("\n---\n", matches.Select(m => m.Content));
            var verdict = await llmAnalysis.CheckConflictAsync(statement, passageB, cancellationToken);
            if (verdict.Relation == ConflictRelation.Contradiction)
                confirmed.Add((file, statement, matches[0], verdict));
        }

        logger.LogInformation("SearchConflict: {Statements} statements checked, {Conflicts} contradictions confirmed",
            statements.Count, confirmed.Count);
        if (truncated)
            logger.LogWarning("SearchConflict: source '{SourceA}' truncated at {Max} statements", sourceAId, MaxStatements);

        var sb = new StringBuilder();
        if (confirmed.Count == 0)
        {
            sb.AppendLine($"Checked {statements.Count} statement(s) from '{sourceAId}' against '{sourceBId}': " +
                          "no genuine contradictions found.");
        }
        else
        {
            sb.AppendLine($"Found {confirmed.Count} confirmed contradiction(s) between '{sourceAId}' and '{sourceBId}' " +
                          $"({statements.Count} statement(s) checked).");
            sb.AppendLine();

            foreach (var (c, i) in confirmed.Select((c, i) => (c, i + 1)))
            {
                sb.AppendLine($"-- Contradiction #{i} --");
                sb.AppendLine($"[A] {c.File}:");
                sb.AppendLine($"    {c.Statement}");
                sb.AppendLine($"[B] {c.Match.FilePath} (chunk {c.Match.ChunkIndex}):");
                var previewB = c.Match.Content.Length > 300 ? c.Match.Content[..300] + "..." : c.Match.Content;
                sb.AppendLine($"    {previewB.ReplaceLineEndings(" ")}");
                sb.AppendLine($"Conflict: {c.Verdict.Explanation}");
                sb.AppendLine();
            }
        }

        if (noMatch > 0)
            sb.AppendLine($"{noMatch} statement(s) had no matching passage in '{sourceBId}' (not a conflict — use compare_sources for gaps).");
        if (truncated)
            sb.AppendLine($"WARNING: '{sourceAId}' contains more than {MaxStatements} statements; only the first ones were checked. " +
                          "Narrow the run with a query, or split the source.");

        return sb.ToString().TrimEnd();
    }

    private async Task<List<(string File, List<string> Passages)>> GetSourceAPassagesAsync(
        string sourceAId, string? query, int topK, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            var chunks = await ragStore.GetAllChunksAsync(sourceAId, cancellationToken);
            return chunks.GroupBy(c => c.FilePath)
                .Select(g => (g.Key, g.OrderBy(c => c.ChunkIndex).Select(c => c.Content).ToList()))
                .ToList();
        }

        var relevant = await ragService.SearchBySourcesAsync(query, topK, [sourceAId], cancellationToken);
        return relevant.GroupBy(r => r.FilePath)
            .Select(g => (g.Key, g.OrderBy(r => r.ChunkIndex).Select(r => r.Content).ToList()))
            .ToList();
    }
}
