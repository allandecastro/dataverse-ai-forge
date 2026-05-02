using Microsoft.Extensions.AI;

namespace DataverseForge.Application.Orchestration;

/// <summary>
/// Trims the message list passed to an LLM call so input-token cost doesn't grow
/// unbounded with session length. The full conversation is still persisted by
/// <c>IConversationStore</c>; this returns a strictly smaller view used for the
/// model invocation only.
///
/// Pruning is by USER-TURN BOUNDARY (not raw message count). A "turn" is one
/// user message plus every assistant + tool message that follows it until the
/// next user message. Keeping turns atomic prevents the orphan-tool-result
/// problem: an assistant ChatMessage that called a tool and the matching tool
/// reply must always travel together, otherwise the LLM rejects the request.
///
/// System messages (the per-agent system prompt) are always preserved at the
/// head of the list.
/// </summary>
public static class HistoryPruner
{
    /// <summary>
    /// Default cap — 20 user turns. Each turn typically generates one assistant
    /// message + zero-to-many tool-call pairs, so the resulting list stays under
    /// ~80 messages even with chatty agents. Plenty for context, well below
    /// typical 128K token windows even with verbose tool results.
    /// </summary>
    public const int DefaultMaxUserTurns = 20;

    /// <summary>
    /// Returns either the original list (if already under <paramref name="maxUserTurns"/>)
    /// or a new list with the oldest user turns removed. System messages at the
    /// head of the input are preserved verbatim.
    /// </summary>
    public static IList<ChatMessage> PruneForLlm(IList<ChatMessage> history, int maxUserTurns = DefaultMaxUserTurns)
    {
        if (history.Count == 0) return history;
        if (maxUserTurns <= 0) return history;

        // Index every user message — those are turn boundaries. If we have fewer
        // user turns than the cap, no pruning needed.
        var userIndexes = new List<int>();
        for (var i = 0; i < history.Count; i++)
        {
            if (history[i].Role == ChatRole.User) userIndexes.Add(i);
        }
        if (userIndexes.Count <= maxUserTurns) return history;

        // Keep system messages at the head + last N turns. The "head" is anything
        // before the first user message (typically the system prompt, possibly
        // multiple if a future change introduces them).
        var firstUserIdx = userIndexes[0];
        var keepFrom = userIndexes[userIndexes.Count - maxUserTurns];

        var trimmed = new List<ChatMessage>(firstUserIdx + (history.Count - keepFrom));
        for (var i = 0; i < firstUserIdx; i++) trimmed.Add(history[i]);
        for (var i = keepFrom; i < history.Count; i++) trimmed.Add(history[i]);
        return trimmed;
    }
}
