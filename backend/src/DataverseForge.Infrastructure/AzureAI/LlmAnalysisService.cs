using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.AzureAI;

/// <summary>
/// <see cref="ILlmAnalysisService"/> implementation backed by a <see cref="Func{IChatClient}"/> factory.
///
/// <para>The factory is invoked on every call so that credential reconfiguration
/// (<c>ChatClientFactory.Reconfigure()</c>) is reflected immediately, even though
/// <see cref="LlmAnalysisService"/> is registered as a singleton.</para>
///
/// <para>All three methods use structured single-line keyword responses followed by a
/// free-text explanation. Parsing scans the entire first line for the keyword (not just
/// the first word) so preambles like <c>"Answer: COVERED"</c> are handled correctly:</para>
/// <list type="bullet">
///   <item><b>ExtractRequirements</b> → asks for a numbered list; each line becomes one requirement.</item>
///   <item><b>CheckCoverage</b> → keyword is <c>COVERED</c>, <c>PARTIAL</c>, or <c>NOT_COVERED</c>.</item>
///   <item><b>CheckConflict</b> → keyword is <c>CONTRADICTION</c>, <c>COMPLEMENT</c>, or <c>UNRELATED</c>.</item>
/// </list>
/// </summary>
public sealed class LlmAnalysisService(Func<IChatClient> clientFactory, ILogger<LlmAnalysisService> logger)
    : ILlmAnalysisService
{
    // Convenience for callers that already have a fixed IChatClient instance (e.g. unit tests).
    public LlmAnalysisService(IChatClient chatClient, ILogger<LlmAnalysisService> logger)
        : this(() => chatClient, logger) { }
    private const string ExtractSystemPrompt =
        "You are a requirements analyst. When given specification text, extract all discrete, " +
        "testable requirements as a numbered list. Each item must be one specific, atomic statement. " +
        "Preserve the original intent. Output only the numbered list — no headings, no prose.";

    private const string CoverageSystemPrompt =
        "You are a requirements coverage analyst. Given a requirement and a list of work-item passages, " +
        "determine whether the requirement is addressed. " +
        "Start your response with exactly one of: COVERED, PARTIAL, or NOT_COVERED — then a brief explanation.";

    private const string ConflictSystemPrompt =
        "You are a document consistency analyst. Passage A is a statement from one source; passage B holds " +
        "the most related passages from another source. Decide whether B contradicts A, complements it, or is unrelated. " +
        "CONTRADICTION: B states something incompatible with A about the same rule — e.g. a different threshold, amount, " +
        "duration, or count, or optional where A says mandatory (or the reverse). " +
        "COMPLEMENT: B is about the same rule and consistent with A (it may add or omit detail). " +
        "UNRELATED: B does not address A's rule — silence is not a contradiction. " +
        "Start your response with exactly one of: CONTRADICTION, COMPLEMENT, or UNRELATED — then a brief explanation " +
        "quoting the conflicting values.";

    public async Task<IReadOnlyList<string>> ExtractRequirementsAsync(
        string specText,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("ExtractRequirements: {Length} chars of spec text", specText.Length);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, ExtractSystemPrompt),
            new(ChatRole.User, $"Extract all requirements from the following specification text:\n\n{specText}")
        };

        var response = await clientFactory().GetResponseAsync(messages, cancellationToken: cancellationToken);
        var text = response.Text ?? string.Empty;

        var requirements = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => System.Text.RegularExpressions.Regex.Replace(line, @"^\d+[\.\)]\s*", "").Trim())
            .Where(line => line.Length > 10)
            .ToList();

        logger.LogDebug("Extracted {Count} requirements", requirements.Count);
        return requirements;
    }

    public async Task<CoverageVerdict> CheckCoverageAsync(
        string requirement,
        IReadOnlyList<string> candidatePassages,
        CancellationToken cancellationToken = default)
    {
        if (candidatePassages.Count == 0)
            return new CoverageVerdict(CoverageStatus.NotCovered, "No candidate passages found in the target source.");

        var passagesText = string.Join("\n\n---\n\n",
            candidatePassages.Select((p, i) => $"[Work item {i + 1}]\n{p}"));

        var userMessage =
            $"Requirement:\n{requirement}\n\n" +
            $"Candidate work-item passages:\n{passagesText}";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, CoverageSystemPrompt),
            new(ChatRole.User, userMessage)
        };

        var response = await clientFactory().GetResponseAsync(messages, cancellationToken: cancellationToken);
        var text = (response.Text ?? string.Empty).Trim();

        return ParseCoverageVerdict(text);
    }

    public async Task<ConflictVerdict> CheckConflictAsync(
        string passageA,
        string passageB,
        CancellationToken cancellationToken = default)
    {
        var userMessage =
            $"Passage A:\n{passageA}\n\n" +
            $"Passage B:\n{passageB}";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, ConflictSystemPrompt),
            new(ChatRole.User, userMessage)
        };

        var response = await clientFactory().GetResponseAsync(messages, cancellationToken: cancellationToken);
        var text = (response.Text ?? string.Empty).Trim();

        return ParseConflictVerdict(text);
    }

    // ─── Parsers ─────────────────────────────────────────────────────────────

    // Scans every word on the first line for the keyword. This handles model preambles such
    // as "Answer: COVERED ..." or "Based on my analysis, COVERED ..." without silently
    // misclassifying them as the default (NotCovered / Unrelated).
    private static string? ExtractKeyword(string text, IReadOnlySet<string> validKeywords)
    {
        var firstLine = text.Split('\n')[0];
        foreach (var word in firstLine.Split([' ', ',', ':', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var upper = word.TrimEnd('.').ToUpperInvariant();
            if (validKeywords.Contains(upper))
                return upper;
        }
        return null;
    }

    private static readonly IReadOnlySet<string> CoverageKeywords =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "COVERED", "PARTIAL", "NOT_COVERED" };

    private static readonly IReadOnlySet<string> ConflictKeywords =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CONTRADICTION", "COMPLEMENT", "UNRELATED" };

    private static CoverageVerdict ParseCoverageVerdict(string text)
    {
        var keyword = ExtractKeyword(text, CoverageKeywords);
        var explanation = text.Trim();

        return keyword switch
        {
            "COVERED" => new CoverageVerdict(CoverageStatus.Covered, explanation),
            "PARTIAL" => new CoverageVerdict(CoverageStatus.PartiallyCovers, explanation),
            _ => new CoverageVerdict(CoverageStatus.NotCovered, explanation)
        };
    }

    private static ConflictVerdict ParseConflictVerdict(string text)
    {
        var keyword = ExtractKeyword(text, ConflictKeywords);
        var explanation = text.Trim();

        return keyword switch
        {
            "CONTRADICTION" => new ConflictVerdict(ConflictRelation.Contradiction, explanation),
            "COMPLEMENT" => new ConflictVerdict(ConflictRelation.Complement, explanation),
            _ => new ConflictVerdict(ConflictRelation.Unrelated, explanation)
        };
    }
}
