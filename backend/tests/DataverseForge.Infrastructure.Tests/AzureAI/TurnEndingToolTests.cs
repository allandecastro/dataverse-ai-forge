using DataverseForge.Infrastructure.AzureAI;
using Microsoft.Extensions.AI;

namespace DataverseForge.Infrastructure.Tests.AzureAI;

/// <summary>
/// After a turn-ending tool the agent's tool loop stops, so the model doesn't get another
/// iteration in which it re-writes the message it already streamed.
/// </summary>
public class TurnEndingToolTests
{
    private static FunctionInvocationContext Context(string name, string result, int functionCount = 1) => new()
    {
        Function = AIFunctionFactory.Create(() => result, name),
        Arguments = new AIFunctionArguments(),
        FunctionCount = functionCount,
    };

    private static async Task<bool> TerminatesAsync(FunctionInvocationContext ctx)
    {
        await ChatClientFactory.InvokeAndStopAfterTurnEndingToolAsync(ctx, CancellationToken.None);
        return ctx.Terminate;
    }

    [Fact]
    public async Task SuccessfulHandoff_EndsTheLoop() =>
        Assert.True(await TerminatesAsync(Context("HandoffToDocumentaryAgent", "Handoff to Documentary Agent initiated. Reason: x")));

    [Fact]
    public async Task RejectedHandoff_KeepsTheLoop_SoTheAgentCanExplain() =>
        Assert.False(await TerminatesAsync(Context("HandoffToSolutionArchitect", "REJECTED: routing loop…")));

    [Fact]
    public async Task SuggestRepliesAlone_EndsTheLoop() =>
        Assert.True(await TerminatesAsync(Context("SuggestReplies", "ok (3 suggestions surfaced)")));

    [Fact]
    public async Task SuggestRepliesAlongsideOtherTools_KeepsTheLoop() =>
        Assert.False(await TerminatesAsync(Context("SuggestReplies", "ok (2 suggestions surfaced)", functionCount: 2)));

    [Fact]
    public async Task OrdinaryTool_KeepsTheLoop() =>
        Assert.False(await TerminatesAsync(Context("GetSolutionsList", "[]")));

    [Fact]
    public async Task ReturnsTheToolResult()
    {
        var ctx = Context("GetSolutionsList", "the-result");
        var result = await ChatClientFactory.InvokeAndStopAfterTurnEndingToolAsync(ctx, CancellationToken.None);
        Assert.Equal("the-result", result?.ToString());
    }
}
