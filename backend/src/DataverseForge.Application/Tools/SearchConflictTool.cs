using System.ComponentModel;
using System.Text;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Tool exposed to DocumentaryAgent.
/// Detects confirmed contradictions between two ingested sources using a two-pass approach:
/// <list type="bullet">
///   <item>Pass 1 (fast): cosine similarity band [0.45, 0.72] pre-filters candidate pairs.</item>
///   <item>Pass 2 (accurate): LLM confirms whether each candidate is a genuine contradiction.</item>
/// </list>
/// This avoids false positives (similar wording but consistent content) that
/// pure cosine similarity would flag incorrectly.
/// </summary>
public sealed class SearchConflictTool(
    RagService ragService,
    ILlmAnalysisService llmAnalysis,
    ILogger<SearchConflictTool> logger)
{
    [Description("Find genuine contradictions between two ingested sources. " +
                 "Uses RAG similarity to find candidates then LLM reasoning to confirm real conflicts. " +
                 "Useful for detecting where the implementation diverges from the spec, " +
                 "or where two spec versions disagree. " +
                 "Both sources must be ingested before calling. " +
                 "Returns only confirmed contradictions with explanations.")]
    public async Task<string> SearchConflictAsync(
        [Description("Source ID of the first document to compare (e.g. specs).")] string sourceAId,
        [Description("Source ID of the second document to compare against (e.g. exports or backlog).")] string sourceBId,
        [Description("Optional topic or area to focus on (e.g. invoice validation rules). Omit to scan all content.")] string? query = null,
        [Description("Maximum number of candidates to examine (default 20). Higher = more thorough but slower.")] int topK = 20,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("SearchConflict called: A='{SourceA}' vs B='{SourceB}' query='{Query}'",
            sourceAId, sourceBId, query ?? "(all)");

        // Pass 1: cosine similarity pre-filter
        var candidates = await ragService.SearchConflictAsync(
            sourceAId, sourceBId, query, topK: topK, cancellationToken: cancellationToken);

        if (candidates.Count == 0)
        {
            var scope = query is null ? "all content" : $"content related to '{query}'";
            return $"No candidate conflict pairs found between '{sourceAId}' and '{sourceBId}' for {scope}. " +
                   "Either the sources cover different topics, or their content is very similar (no semantic band overlap).";
        }

        logger.LogInformation("Pass 1 found {Count} candidates; running LLM confirmation", candidates.Count);

        // Pass 2: LLM confirmation
        var confirmed = new List<(ConflictCandidate Candidate, ConflictVerdict Verdict)>();
        var discarded = 0;

        foreach (var candidate in candidates)
        {
            var verdict = await llmAnalysis.CheckConflictAsync(
                candidate.ContentA, candidate.ContentB, cancellationToken);

            if (verdict.Relation == ConflictRelation.Contradiction)
                confirmed.Add((candidate, verdict));
            else
                discarded++;

            logger.LogDebug("Candidate {FileA}/{ChunkA} vs {FileB}/{ChunkB}: {Relation}",
                candidate.FilePathA, candidate.ChunkIndexA,
                candidate.FilePathB, candidate.ChunkIndexB,
                verdict.Relation);
        }

        if (confirmed.Count == 0)
        {
            return $"Examined {candidates.Count} candidate pairs; the LLM confirmed 0 genuine contradictions " +
                   $"between '{sourceAId}' and '{sourceBId}'. " +
                   $"The {discarded} candidate(s) were classified as complementary or unrelated.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Found {confirmed.Count} confirmed contradiction(s) between '{sourceAId}' and '{sourceBId}'.");
        sb.AppendLine($"({discarded} of {candidates.Count} candidates were discarded after LLM review.)");
        sb.AppendLine();

        foreach (var (c, v, i) in confirmed.Select((x, i) => (x.Candidate, x.Verdict, i + 1)))
        {
            sb.AppendLine($"-- Contradiction #{i} --");
            sb.AppendLine($"[A] {c.FilePathA} (chunk {c.ChunkIndexA}):");
            var previewA = c.ContentA.Length > 300 ? c.ContentA[..300] + "..." : c.ContentA;
            sb.AppendLine($"    {previewA.ReplaceLineEndings(" ")}");
            sb.AppendLine($"[B] {c.FilePathB} (chunk {c.ChunkIndexB}):");
            var previewB = c.ContentB.Length > 300 ? c.ContentB[..300] + "..." : c.ContentB;
            sb.AppendLine($"    {previewB.ReplaceLineEndings(" ")}");
            sb.AppendLine($"Conflict: {v.Explanation}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}