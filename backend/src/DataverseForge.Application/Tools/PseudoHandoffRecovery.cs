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
        ["reviewer"]           = (AgentNames.Reviewer, "Reviewer"),
        ["tester"]             = (AgentNames.Tester, "Tester"),
    };

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
        var match = HandoffPattern.Match(trimmed);
        if (!match.Success) return responseText;

        // Only recover when the WHOLE response is the pseudo-call (allowing
        // a closing paren and optional trailing whitespace). A reply with
        // real prose around the call is too risky to auto-fire.
        if (!trimmed.EndsWith(")", StringComparison.Ordinal)) return responseText;

        var snakeName = match.Groups[1].Value.Replace("_", "", StringComparison.Ordinal);
        if (!SnakeNameMap.TryGetValue(snakeName, out var target))
        {
            logger.LogWarning("PseudoHandoffRecovery: unknown target '{SnakeName}' — leaving text untouched", match.Groups[1].Value);
            return responseText;
        }

        // Best-effort reason extraction. Fall back to a generic message if
        // we can't pull anything readable out.
        var reasonMatch = ReasonArgPattern.Match(trimmed);
        var reason = reasonMatch.Success
            ? Regex.Unescape(reasonMatch.Groups[2].Value)
            : "Recovered from pseudo-handoff text — original tool call was malformed.";

        logger.LogWarning(
            "PseudoHandoffRecovery: detected pseudo-handoff text targeting {Target}, firing real handoff with reason='{Reason}'",
            target.DisplayName, reason);

        await HandoffTool.ExecuteHandoffAsync(
            projectStateStore, sessionContext,
            target.AgentName, target.DisplayName,
            reason, cancellationToken);

        return $"Handing this to the {target.DisplayName} — {reason}";
    }
}
