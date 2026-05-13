using System.Text.RegularExpressions;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Defensive net for the recurring "agent emits handoff_to_X(...) as plain
/// chat text" bug. Despite the prompt golden rule, models sometimes still
/// produce a string like
///     handoff_to_solution_architect(requirementSpec="...")
/// as their entire chat reply, with no actual tool call attached. The user
/// sees pseudocode and the conversation is stuck because no handoff event
/// fires.
///
/// This helper sniffs the agent's final text and, if the pattern matches,
/// performs the handoff server-side and replaces the message with a clean
/// natural-language acknowledgement. It is best-effort; if anything looks
/// off (unknown target name, message contains real prose around the call)
/// the original text is returned unchanged.
/// </summary>
public static class PseudoHandoffRecovery
{
    // Match `name(` at the start of the (trimmed) response — captures the
    // tool name. We don't bother parsing arguments — the actual handoff
    // tool accepts any of `reason` / `summary` / canonical, and we can fall
    // back to a generic reason if extracting fails.
    private static readonly Regex HandoffPattern = new(
        @"^\s*handoff_to_([a-z_]+)\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Narrative-English variant of the same bug. Real session 2026-05-06
    // 18:05: SA wrote "Handing off to UI Builder to add the Account, Contact,
    // and eppc_gift tables to the Phase3 Verify app navigation" with no
    // actual handoff_to_ui_builder tool call. The function-call regex above
    // misses this entirely — pure prose, no parens. We only fire on this
    // pattern when (a) the message STARTS with the handoff phrase (so prose
    // mid-message that happens to mention "handing off" doesn't trigger),
    // and (b) the targeted agent name is one we recognize.
    private static readonly Regex NarrativeHandoffPattern = new(
        @"^\s*(?:I\s+(?:will|am|'m)\s+)?(?:hand(?:ing)?(?:\s+(?:this|it))?\s+(?:off\s+)?(?:to|over\s+to)|handoff\s+to)\s+(?:the\s+)?(solution\s+architect|business\s+analyst|data\s+model\s+builder|data\s+population|ui\s+builder|tester|security\s+builder|automation\s+builder)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ReasonArgPattern = new(
        @"(requirementSpec|designSpec|schemaSpec|uiSpec|context|reason|summary)\s*[=:]\s*""((?:[^""\\]|\\.)*)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, (string AgentName, string DisplayName)> SnakeNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["solution_architect"] = (AgentNames.SolutionArchitect, "Solution Architect"),
        ["solutionarchitect"]  = (AgentNames.SolutionArchitect, "Solution Architect"),
        ["business_analyst"]   = (AgentNames.BusinessAnalyst, "Business Analyst"),
        ["businessanalyst"]    = (AgentNames.BusinessAnalyst, "Business Analyst"),
        ["data_model_builder"] = (AgentNames.DataModelBuilder, "Data Model Builder"),
        ["datamodelbuilder"]   = (AgentNames.DataModelBuilder, "Data Model Builder"),
        ["data_population"]    = (AgentNames.DataPopulation, "Data Population"),
        ["datapopulation"]     = (AgentNames.DataPopulation, "Data Population"),
        ["ui_builder"]         = (AgentNames.UIBuilder, "UI Builder"),
        ["uibuilder"]          = (AgentNames.UIBuilder, "UI Builder"),
        ["tester"]             = (AgentNames.Tester, "Tester"),
        ["security_builder"]   = (AgentNames.SecurityBuilder, "Security Builder"),
        ["securitybuilder"]    = (AgentNames.SecurityBuilder, "Security Builder"),
        ["automation_builder"] = (AgentNames.AutomationBuilder, "Automation Builder"),
        ["automationbuilder"]  = (AgentNames.AutomationBuilder, "Automation Builder"),
    };

    // Display-name → agent map for the narrative-English path. The regex
    // captures the human-readable name verbatim ("ui builder", "data model
    // builder") — normalize-and-lookup here so we don't have to repeat the
    // whitespace variants in the map.
    private static (string AgentName, string DisplayName)? ResolveDisplayName(string captured)
    {
        var normalized = string.Concat(captured.Where(c => !char.IsWhiteSpace(c)));
        return SnakeNameMap.TryGetValue(normalized, out var hit) ? hit : null;
    }

    /// <summary>
    /// If <paramref name="responseText"/> looks like a pseudo-handoff call, do
    /// the handoff and return a cleaned reply. Otherwise return the input
    /// unchanged. Only triggers when the *entire* response is essentially the
    /// pseudo-call — a normal sentence that happens to mention a tool name
    /// will not match.
    /// </summary>
    public static async Task<string> RecoverAsync(
        string responseText,
        IProjectStateStore projectStateStore,
        ISessionContext sessionContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return responseText;

        var trimmed = responseText.Trim();

        // Path 1 — function-call pseudocode: `handoff_to_X(...)` as the
        // ENTIRE reply. Tightest possible match; only fires when the whole
        // text is the pseudo-call (closing paren required).
        var fnMatch = HandoffPattern.Match(trimmed);
        if (fnMatch.Success && trimmed.EndsWith(")", StringComparison.Ordinal))
        {
            var snakeName = fnMatch.Groups[1].Value.Replace("_", "", StringComparison.Ordinal);
            if (!SnakeNameMap.TryGetValue(snakeName, out var fnTarget))
            {
                logger.LogWarning("PseudoHandoffRecovery: unknown target '{SnakeName}' — leaving text untouched", fnMatch.Groups[1].Value);
                return responseText;
            }

            var fnReasonMatch = ReasonArgPattern.Match(trimmed);
            var fnReason = fnReasonMatch.Success
                ? Regex.Unescape(fnReasonMatch.Groups[2].Value)
                : "Recovered from pseudo-handoff text — original tool call was malformed.";

            logger.LogWarning(
                "PseudoHandoffRecovery: detected pseudo-handoff text targeting {Target}, firing real handoff with reason='{Reason}'",
                fnTarget.DisplayName, fnReason);

            await HandoffTool.ExecuteHandoffAsync(
                projectStateStore, sessionContext,
                fnTarget.AgentName, fnTarget.DisplayName,
                fnReason, cancellationToken);

            return $"Handing this to the {fnTarget.DisplayName} — {fnReason}";
        }

        // Path 2 — narrative-English: "Handing off to UI Builder to add the
        // tables…" with no actual tool call. Real session 2026-05-06 18:05
        // had SA do exactly this and the conversation stalled. We only fire
        // when the message STARTS with the handoff phrase so an unrelated
        // sentence ("…and then I'll be handing off to UI Builder afterwards")
        // doesn't trigger.
        var narrativeMatch = NarrativeHandoffPattern.Match(trimmed);
        if (narrativeMatch.Success)
        {
            var resolved = ResolveDisplayName(narrativeMatch.Groups[1].Value);
            if (resolved is null)
            {
                logger.LogWarning("PseudoHandoffRecovery: narrative match but unknown target '{Captured}' — leaving text untouched", narrativeMatch.Groups[1].Value);
                return responseText;
            }
            var narrativeTarget = resolved.Value;

            // Reason = the rest of the message after the handoff phrase, up
            // to the first sentence boundary. Best-effort — we just want
            // something readable for the audit log.
            var afterMatch = trimmed.Substring(narrativeMatch.Index + narrativeMatch.Length).TrimStart();
            var sentenceEnd = afterMatch.IndexOfAny(['.', '!', '\n', '\r']);
            var rawReason = sentenceEnd > 0 ? afterMatch[..sentenceEnd] : afterMatch;
            rawReason = rawReason.Trim().TrimStart('—', '-', ':', ',', ' ');
            var reason = string.IsNullOrWhiteSpace(rawReason)
                ? "Recovered from narrative handoff text — original tool call was missing."
                : rawReason;

            logger.LogWarning(
                "PseudoHandoffRecovery: detected NARRATIVE pseudo-handoff targeting {Target}, firing real handoff with reason='{Reason}'",
                narrativeTarget.DisplayName, reason);

            await HandoffTool.ExecuteHandoffAsync(
                projectStateStore, sessionContext,
                narrativeTarget.AgentName, narrativeTarget.DisplayName,
                reason, cancellationToken);

            return $"Handing this to the {narrativeTarget.DisplayName} — {reason}";
        }

        return responseText;
    }
}
