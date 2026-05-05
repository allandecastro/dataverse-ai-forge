using System.Text.RegularExpressions;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Cleans agent text content before it reaches the user. Two recurring issues
/// the sanitizer absorbs:
///
/// 1. Runaway repetition. The Tester (and occasionally other agents) sometimes
///    emits the same VERIFICATION REPORT — or the same paragraph — multiple
///    times within a single response. Real session 2026-05-05 had the Tester
///    repeat its FAIL report 30+ times in one bubble. This is the model's own
///    looping inside one generation, NOT the framework's tool-call loop.
///    We keep the first occurrence and drop the rest.
///
/// 2. Raw JSON dumps in chat. The Data Population agent has been observed
///    pasting the actual records-array JSON it's about to send, which is just
///    noise from a user's perspective ("we don't care about JSON of the data
///    agent" — direct user feedback 2026-05-05). The sanitizer strips fenced
///    code blocks whose first non-whitespace character is `[` or `{` AND
///    whose body parses as a JSON array of objects.
///
/// Both passes are best-effort: if the input doesn't match a known noise
/// pattern, it is returned unchanged.
/// </summary>
public static class AgentResponseSanitizer
{
    // Match a "VERIFICATION REPORT" header at the start of a line so we can
    // count occurrences and trim duplicates. We accept any whitespace/heading
    // markers around it. Multiline because reports are line-oriented.
    private static readonly Regex VerificationReportHeader = new(
        @"^[\s>#*]*VERIFICATION\s+REPORT\b",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Fenced code blocks. We only strip ones that begin with `[` or `{` AND
    // contain at least one quoted property (a heuristic for "this is JSON
    // data" rather than "this is a snippet I'm explaining"). The (?s) flag
    // makes `.` match newlines.
    private static readonly Regex JsonCodeBlock = new(
        "(?s)```(?:json)?\\s*(?<body>[\\[{].*?[\\]}])\\s*```",
        RegexOptions.Compiled);

    // Numbered or bulleted list whose items are bare GUIDs. Real session
    // 2026-05-05: SA listed 5 contact GUIDs to the user as a numbered list
    // ("1. 362f3de3-48d8-…  2. 2104801a-…"), which is just noise. Match
    // 2+ consecutive list items where the item content is a GUID.
    private static readonly Regex GuidListBlock = new(
        @"(?:^[\s>]*(?:\d+\.|[-*])\s+[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\s*\r?\n){2,}",
        RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Apply all sanitization passes. Pass <paramref name="agentName"/> so the
    /// JSON-stripping pass only runs on agents where we know it's noise.
    /// </summary>
    public static string Clean(string text, string? agentName = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var cleaned = TrimRepeatedVerificationReports(text);
        cleaned = RedactBareGuidLists(cleaned);
        if (IsDataPopulationAgent(agentName))
        {
            cleaned = StripJsonRecordsDumps(cleaned);
        }
        return cleaned;
    }

    /// <summary>
    /// Replace a bullet/number list of bare GUIDs with a single-line summary.
    /// Useful when an agent dumps "I retrieved 5 contact IDs: 1. <guid> 2. <guid>…"
    /// — those are internal references, not user-facing detail.
    /// </summary>
    public static string RedactBareGuidLists(string text)
    {
        return GuidListBlock.Replace(text, m =>
        {
            // Count GUIDs in the block. Each line ends with \n so split-and-trim works.
            var guidCount = m.Value.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Count(l => l.TrimEnd().Length > 0);
            return $"_(showing {guidCount} record IDs — referenced internally; "
                + "ask for record names if you want to see them)_\n";
        });
    }

    private static bool IsDataPopulationAgent(string? agentName) =>
        string.Equals(agentName, "DataPopulation", StringComparison.Ordinal);

    /// <summary>
    /// If the text contains 2+ "VERIFICATION REPORT" headers, truncate at the
    /// END of the first report (i.e. just before the second header starts) and
    /// drop everything after. The model has lost its place; the user only
    /// needs to see one report. Adds a short footer so reviewers know
    /// truncation happened.
    /// </summary>
    public static string TrimRepeatedVerificationReports(string text)
    {
        var matches = VerificationReportHeader.Matches(text);
        if (matches.Count < 2) return text;

        // Cut just before the SECOND occurrence's leading whitespace.
        var secondHeaderStart = matches[1].Index;
        // Walk back over leading whitespace / `<think>` opens so the cut
        // doesn't leave a dangling open tag.
        var cutAt = secondHeaderStart;
        while (cutAt > 0 && char.IsWhiteSpace(text[cutAt - 1])) cutAt--;

        var trimmed = text[..cutAt].TrimEnd();
        return trimmed
            + "\n\n_(Output was truncated — the agent repeated this report "
            + $"{matches.Count} times in a single response; showing the first.)_";
    }

    /// <summary>
    /// Remove fenced code blocks whose body is a JSON array/object — these
    /// are typically the literal records payload the Data Population agent
    /// pastes mid-conversation. Surrounding prose is preserved.
    /// </summary>
    public static string StripJsonRecordsDumps(string text)
    {
        return JsonCodeBlock.Replace(text, m =>
        {
            var body = m.Groups["body"].Value.TrimStart();
            // Heuristic: only treat as a "records dump" if it starts as an
            // array AND contains a quoted property — single object literals
            // like { eppc_giftname: 'X' } that happen to be the worked
            // example we want to keep are rejected.
            if (body.Length == 0) return m.Value;
            if (body[0] != '[') return m.Value;
            if (!body.Contains('"', StringComparison.Ordinal)) return m.Value;
            return "_(records JSON omitted from chat — full payload is staged "
                + "server-side under the planId)_";
        });
    }
}
