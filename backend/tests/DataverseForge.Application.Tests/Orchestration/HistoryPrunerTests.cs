using DataverseForge.Application.Orchestration;
using Microsoft.Extensions.AI;

namespace DataverseForge.Application.Tests.Orchestration;

public class HistoryPrunerTests
{
    private static ChatMessage Sys(string text) => new(ChatRole.System, text);
    private static ChatMessage Usr(string text) => new(ChatRole.User, text);
    private static ChatMessage Asst(string text) => new(ChatRole.Assistant, text);
    private static ChatMessage Tool(string text) => new(ChatRole.Tool, text);

    [Fact]
    public void Empty_history_passes_through()
    {
        var history = new List<ChatMessage>();
        var result = HistoryPruner.PruneForLlm(history);
        Assert.Same(history, result);
    }

    [Fact]
    public void History_under_cap_passes_through_unchanged()
    {
        var history = new List<ChatMessage>
        {
            Sys("system prompt"),
            Usr("turn 1"),
            Asst("response 1"),
            Usr("turn 2"),
            Asst("response 2"),
        };
        var result = HistoryPruner.PruneForLlm(history, maxUserTurns: 5);
        Assert.Same(history, result);
    }

    [Fact]
    public void Drops_oldest_user_turns_when_over_cap()
    {
        // 6 user turns, cap 3 → keep last 3 turns + system prompt at head.
        var history = new List<ChatMessage>
        {
            Sys("system prompt"),
            Usr("u1"), Asst("a1"),
            Usr("u2"), Asst("a2"),
            Usr("u3"), Asst("a3"),
            Usr("u4"), Asst("a4"),
            Usr("u5"), Asst("a5"),
            Usr("u6"), Asst("a6"),
        };
        var result = HistoryPruner.PruneForLlm(history, maxUserTurns: 3);

        // Expected: system + u4..a6 (last 3 turns).
        Assert.Equal(7, result.Count);
        Assert.Equal(ChatRole.System, result[0].Role);
        Assert.Equal("u4", GetText(result[1]));
        Assert.Equal("a4", GetText(result[2]));
        Assert.Equal("u5", GetText(result[3]));
        Assert.Equal("a5", GetText(result[4]));
        Assert.Equal("u6", GetText(result[5]));
        Assert.Equal("a6", GetText(result[6]));
    }

    [Fact]
    public void Keeps_tool_call_and_tool_result_in_same_turn()
    {
        // Symptom this protects: pruning by raw message count would split an
        // assistant tool-call message from its tool result, leaving the model
        // with an orphan ChatRole.Tool that triggers a 400 from the API.
        var history = new List<ChatMessage>
        {
            Sys("system"),
            Usr("u1"), Asst("a1"),
            Usr("u2"),
                Asst("calling get_table"),  // assistant invokes a tool
                Tool("table data"),          // tool result anchored to the assistant
                Asst("here is the data"),
            Usr("u3"), Asst("a3"),
        };
        var result = HistoryPruner.PruneForLlm(history, maxUserTurns: 2);

        // Should keep last 2 turns: u2 (with its tool exchange) + u3.
        Assert.Equal(7, result.Count);
        Assert.Equal(ChatRole.System, result[0].Role);
        Assert.Equal("u2", GetText(result[1]));
        Assert.Equal("calling get_table", GetText(result[2]));
        Assert.Equal(ChatRole.Tool, result[3].Role);
        Assert.Equal("here is the data", GetText(result[4]));
        Assert.Equal("u3", GetText(result[5]));
        Assert.Equal("a3", GetText(result[6]));
    }

    [Fact]
    public void Preserves_multiple_system_messages_at_head()
    {
        // Defensive: future change might introduce multiple system messages
        // (e.g. agent prompt + a per-turn directive). All should survive.
        var history = new List<ChatMessage>
        {
            Sys("language directive"),
            Sys("agent role"),
            Usr("u1"), Asst("a1"),
            Usr("u2"), Asst("a2"),
            Usr("u3"), Asst("a3"),
        };
        var result = HistoryPruner.PruneForLlm(history, maxUserTurns: 1);

        Assert.Equal(4, result.Count);
        Assert.Equal(ChatRole.System, result[0].Role);
        Assert.Equal(ChatRole.System, result[1].Role);
        Assert.Equal("u3", GetText(result[2]));
        Assert.Equal("a3", GetText(result[3]));
    }

    [Fact]
    public void Returns_original_when_max_is_zero_or_negative()
    {
        // Defensive: a misconfigured cap shouldn't disable history entirely;
        // returning the original is a safe no-op.
        var history = new List<ChatMessage> { Sys("s"), Usr("u"), Asst("a") };
        Assert.Same(history, HistoryPruner.PruneForLlm(history, maxUserTurns: 0));
        Assert.Same(history, HistoryPruner.PruneForLlm(history, maxUserTurns: -5));
    }

    [Fact]
    public void Default_cap_is_DefaultMaxUserTurns()
    {
        // Constructing exactly DefaultMaxUserTurns + 1 user turns should trigger
        // pruning to exactly DefaultMaxUserTurns when the default is used.
        var history = new List<ChatMessage> { Sys("s") };
        for (var i = 0; i <= HistoryPruner.DefaultMaxUserTurns; i++)
        {
            history.Add(Usr($"u{i}"));
            history.Add(Asst($"a{i}"));
        }
        var result = HistoryPruner.PruneForLlm(history);
        // 1 system + DefaultMaxUserTurns turns × 2 messages each.
        Assert.Equal(1 + (HistoryPruner.DefaultMaxUserTurns * 2), result.Count);
        Assert.Equal(ChatRole.System, result[0].Role);
        // First non-system message should be the user message at the new boundary.
        Assert.Equal($"u1", GetText(result[1]));
    }

    private static string GetText(ChatMessage msg) =>
        msg.Contents.OfType<TextContent>().FirstOrDefault()?.Text ?? string.Empty;
}
