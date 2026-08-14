using System.ComponentModel;
using System.Text;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Tool exposed to DocumentaryAgent.
/// Performs a reliable gap analysis by combining RAG retrieval with LLM reasoning:
/// <list type="bullet">
///   <item>The LLM extracts discrete requirements from the spec source (not just chunk boundaries).</item>
///   <item>Each requirement is searched against the coverage source via RAG.</item>
///   <item>The LLM confirms whether each requirement is truly covered by the search results.</item>
/// </list>
/// This two-pass approach avoids false positives (cosine-only can match similar wording
/// but different intent) and false negatives (chunking boundaries can split related content).
/// </summary>
public sealed class CompareSourcesTool(
    RagService ragService,
    IRagStore ragStore,
    ILlmAnalysisService llmAnalysis,
    ILogger<CompareSourcesTool> logger)
{
    private const int SearchTopK = 5;

    [Description("Reliable gap analysis: find requirements in the spec source that are NOT covered by work items " +
                 "in the coverage source. Uses LLM reasoning to extract discrete requirements and confirm coverage " +
                 "not just cosine similarity. " +
                 "Use when the user asks what is in the spec but not in the backlog. " +
                 "Both sources must be ingested first.")]
    public async Task<string> CompareSourcesAsync(
        [Description("Source ID of the reference document (e.g. a spec or client doc).")] string specSourceId,
        [Description("Source ID of the coverage target (e.g. the AzDo backlog).")] string backlogSourceId,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("CompareSourcesTool (reliable): spec='{Spec}' vs backlog='{Backlog}'",
            specSourceId, backlogSourceId);

        var specChunks = await ragStore.GetAllChunksAsync(specSourceId, cancellationToken);

        if (specChunks.Count == 0)
            return $"No indexed content found for source '{specSourceId}'. " +
                   "Ingest it first using the ingest_documents tool, then retry.";

        var requirementsByFile = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var fileGroup in specChunks.GroupBy(c => c.FilePath))
        {
            var fileText = string.Join("\n\n", fileGroup.Select(c => c.Content));
            var requirements = await llmAnalysis.ExtractRequirementsAsync(fileText, cancellationToken);
            if (requirements.Count > 0)
                requirementsByFile[fileGroup.Key] = requirements;
            logger.LogDebug("File '{File}': {Count} requirements extracted", fileGroup.Key, requirements.Count);
        }

        var totalRequirements = requirementsByFile.Values.Sum(r => r.Count);
        if (totalRequirements == 0)
            return $"No discrete requirements could be extracted from '{specSourceId}'.";

        var backlogIds = new[] { backlogSourceId };
        var results = new List<(string File, string Requirement, CoverageVerdict Verdict)>();

        foreach (var (file, requirements) in requirementsByFile)
        {
            foreach (var requirement in requirements)
            {
                var candidates = await ragService.SearchBySourcesAsync(
                    requirement, SearchTopK, backlogIds, cancellationToken);
                var candidateTexts = candidates.Select(r => r.Content).ToList();
                var verdict = await llmAnalysis.CheckCoverageAsync(requirement, candidateTexts, cancellationToken);
                results.Add((file, requirement, verdict));
            }
        }

        var notCovered = results.Where(r => r.Verdict.Status == CoverageStatus.NotCovered).ToList();
        var partial = results.Where(r => r.Verdict.Status == CoverageStatus.PartiallyCovers).ToList();
        var covered = results.Count(r => r.Verdict.Status == CoverageStatus.Covered);

        var sb = new StringBuilder();
        sb.AppendLine($"Gap analysis: '{specSourceId}' vs '{backlogSourceId}'");
        sb.AppendLine($"Total requirements analysed: {totalRequirements}");
        sb.AppendLine($"Covered: {covered} | Partial: {partial.Count} | Not covered: {notCovered.Count}");
        sb.AppendLine();

        if (notCovered.Count == 0 && partial.Count == 0)
        {
            sb.AppendLine("All requirements appear to be covered by the backlog. No gaps found.");
            return sb.ToString().TrimEnd();
        }

        if (notCovered.Count > 0)
        {
            sb.AppendLine("## Not covered");
            foreach (var (file, req, verdict) in notCovered.GroupBy(r => r.File).SelectMany(g => g))
            {
                sb.AppendLine($"File: {file}");
                sb.AppendLine($"  * {req}");
                sb.AppendLine($"    -> {verdict.Explanation}");
            }
            sb.AppendLine();
        }

        if (partial.Count > 0)
        {
            sb.AppendLine("## Partially covered");
            foreach (var (file, req, verdict) in partial.GroupBy(r => r.File).SelectMany(g => g))
            {
                sb.AppendLine($"File: {file}");
                sb.AppendLine($"  * {req}");
                sb.AppendLine($"    -> {verdict.Explanation}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Consider creating or updating work items for the uncovered and partially covered requirements above.");
        return sb.ToString().TrimEnd();
    }
}