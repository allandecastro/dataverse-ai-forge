using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.AzureAI;

/// <summary>
/// <see cref="ILlmAnalysisService"/> implementation backed by an <see cref="IChatClient"/>.
///
/// <para>All three methods use structured single-line keyword responses followed by a
/// free-text explanation so parsing is robust even when the model adds prose around
/// the answer:</para>
/// <list type="bullet">
///   <item><b>ExtractRequirements</b> → asks for a numbered list; each line becomes one requirement.</item>
///   <item><b>CheckCoverage</b> → first word is <c>COVERED</c>, <c>PARTIAL</c>, or <c>NOT_COVERED</c>.</item>
///   <item><b>CheckConflict</b> → first word is <c>CONTRADICTION</c>, <c>COMPLEMENT</c>, or <c>UNRELATED</c>.</item>
/// </list>
/// </summary>
public sealed class LlmAnalysisService(IChatClient chatClient, ILogger<LlmAnalysisService> logger)
    : ILlmAnalysisService
{
    private const string ExtractSystemPrompt =
        "You are a requirements analyst. When given specification text, extract all discrete, " +
        "testable requirements as a numbered list. Each item must be one specific, atomic statement. " +
        "Preserve the original intent. Output only the numbered list — no headings, no prose.";

    private const string CoverageSystemPrompt =
        "You are a requirements coverage analyst. Given a requirement and a list of work-item passages, " +
        "determine whether the requirement is addressed. " +
        "Start your response with exactly one of: COVERED, PARTIAL, or NOT_COVERED — then a brief explanation.";

    private const string ConflictSystemPrompt =
        "You are a document consistency analyst. Given two passages from different sources, " +
        "determine whether they contradict each other, complement each other, or are unrelated. " +
        "Start your response with exactly one of: CONTRADICTION, COMPLEMENT, or UNRELATED — then a brief explanation.";

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

        var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
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

        var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
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

        var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        var text = (response.Text ?? string.Empty).Trim();

        return ParseConflictVerdict(text);
    }

    // ─── Parsers ─────────────────────────────────────────────────────────────

    private static CoverageVerdict ParseCoverageVerdict(string text)
    {
        var firstWord = text.Split([' ', '\n'], 2)[0].ToUpperInvariant();
        var explanation = text.Length > firstWord.Length
            ? text[(firstWord.Length + 1)..].Trim()
            : text;

        return firstWord switch
        {
            "COVERED" => new CoverageVerdict(CoverageStatus.Covered, explanation),
            "PARTIAL" => new CoverageVerdict(CoverageStatus.PartiallyCovers, explanation),
            _ => new CoverageVerdict(CoverageStatus.NotCovered, explanation)
        };
    }

    private static ConflictVerdict ParseConflictVerdict(string text)
    {
        var firstWord = text.Split([' ', '\n'], 2)[0].ToUpperInvariant();
        var explanation = text.Length > firstWord.Length
            ? text[(firstWord.Length + 1)..].Trim()
            : text;

        return firstWord switch
        {
            "CONTRADICTION" => new ConflictVerdict(ConflictRelation.Contradiction, explanation),
            "COMPLEMENT" => new ConflictVerdict(ConflictRelation.Complement, explanation),
            _ => new ConflictVerdict(ConflictRelation.Unrelated, explanation)
        };
    }
}
