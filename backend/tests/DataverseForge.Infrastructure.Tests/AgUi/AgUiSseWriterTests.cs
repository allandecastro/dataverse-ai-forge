using System.Text;
using System.Text.Json;
using DataverseForge.Infrastructure.AgUi;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;

namespace DataverseForge.Infrastructure.Tests.AgUi;

public class AgUiSseWriterTests
{
    private static (DefaultHttpContext Context, MemoryStream Body) CreateMockHttpResponse()
    {
        var body = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;
        return (context, body);
    }

    private static List<JsonElement> ParseSseEvents(MemoryStream body)
    {
        body.Position = 0;
        var text = Encoding.UTF8.GetString(body.ToArray());
        var events = new List<JsonElement>();

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                var json = line["data: ".Length..];
                events.Add(JsonSerializer.Deserialize<JsonElement>(json));
            }
        }

        return events;
    }

    private static ChatResponseUpdate TextUpdate(string text)
    {
        var update = new ChatResponseUpdate();
        update.Contents.Add(new TextContent(text));
        return update;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateStream(params string[] texts)
    {
        foreach (var text in texts)
        {
            yield return TextUpdate(text);
            await Task.CompletedTask;
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateStreamWithUsage(
        string text, int inputTokens, int outputTokens)
    {
        yield return TextUpdate(text);
        yield return new ChatResponseUpdate
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["inputTokens"] = inputTokens,
                ["outputTokens"] = outputTokens
            }
        };
        await Task.CompletedTask;
    }

    /// <summary>
    /// Finds the value from a JSON Patch array delta with the given path.
    /// Delta format: [{ "op": "replace", "path": "/activeAgent", "value": "..." }]
    /// </summary>
    private static JsonElement? FindPatchValue(JsonElement deltaArray, string path)
    {
        foreach (var patch in deltaArray.EnumerateArray())
        {
            if (patch.TryGetProperty("path", out var p) && p.GetString() == path)
            {
                if (patch.TryGetProperty("value", out var v))
                    return v;
            }
        }
        return null;
    }

    [Fact]
    public async Task EmitsStateDelta_WithReasoning_WhenThinkTagsPresent()
    {
        var (ctx, body) = CreateMockHttpResponse();
        var stream = CreateStream("<think>I need to check the environment first.</think>", "Here is the response.");

        await AgUiSseWriter.WriteStreamAsync(ctx.Response, "t1", "r1", "SolutionArchitect", stream, CancellationToken.None);

        var events = ParseSseEvents(body);
        var types = events.Select(e => e.GetProperty("type").GetString()).ToList();

        Assert.Contains("STATE_DELTA", types);

        // Find the reasoning STATE_DELTA (delta is a JSON Patch array)
        var reasoningDelta = events.FirstOrDefault(e =>
            e.GetProperty("type").GetString() == "STATE_DELTA" &&
            e.TryGetProperty("delta", out var d) &&
            d.ValueKind == JsonValueKind.Array &&
            FindPatchValue(d, "/reasoning") != null);

        Assert.NotEqual(default, reasoningDelta);
        var reasoning = FindPatchValue(reasoningDelta.GetProperty("delta"), "/reasoning")!.Value.GetString();
        Assert.Equal("I need to check the environment first.", reasoning);

        // Verify text content is sent after reasoning
        var textContent = events.FirstOrDefault(e =>
            e.GetProperty("type").GetString() == "TEXT_MESSAGE_CONTENT");
        Assert.NotEqual(default, textContent);
    }

    [Fact]
    public async Task StreamsTextNormally_WhenNoThinkTags()
    {
        var (ctx, body) = CreateMockHttpResponse();
        var stream = CreateStream("This is a normal response without think tags.");

        await AgUiSseWriter.WriteStreamAsync(ctx.Response, "t1", "r1", "SolutionArchitect", stream, CancellationToken.None);

        var events = ParseSseEvents(body);
        var types = events.Select(e => e.GetProperty("type").GetString()).ToList();

        // Should NOT have a reasoning STATE_DELTA
        var reasoningDeltas = events.Where(e =>
            e.GetProperty("type").GetString() == "STATE_DELTA" &&
            e.TryGetProperty("delta", out var d) &&
            d.ValueKind == JsonValueKind.Array &&
            FindPatchValue(d, "/reasoning") != null).ToList();
        Assert.Empty(reasoningDeltas);

        // Should have normal text flow
        Assert.Contains("TEXT_MESSAGE_START", types);
        Assert.Contains("TEXT_MESSAGE_CONTENT", types);
        Assert.Contains("TEXT_MESSAGE_END", types);
    }

    [Fact]
    public async Task EmitsActiveAgent_AfterRunStarted()
    {
        var (ctx, body) = CreateMockHttpResponse();
        var stream = CreateStream("Hello");

        await AgUiSseWriter.WriteStreamAsync(ctx.Response, "t1", "r1", "BusinessAnalyst", stream, CancellationToken.None);

        var events = ParseSseEvents(body);

        // First event should be RUN_STARTED
        Assert.Equal("RUN_STARTED", events[0].GetProperty("type").GetString());

        // Second event should be STATE_DELTA with activeAgent (JSON Patch format)
        Assert.Equal("STATE_DELTA", events[1].GetProperty("type").GetString());
        var delta = events[1].GetProperty("delta");
        var activeAgent = FindPatchValue(delta, "/activeAgent");
        Assert.NotNull(activeAgent);
        Assert.Equal("BusinessAnalyst", activeAgent.Value.GetString());
    }

    [Fact]
    public async Task IncludesUsageMetadata_InRunFinished()
    {
        var (ctx, body) = CreateMockHttpResponse();
        var stream = CreateStreamWithUsage("This is a response with usage tracking.", 100, 50);

        await AgUiSseWriter.WriteStreamAsync(ctx.Response, "t1", "r1", "SolutionArchitect", stream, CancellationToken.None);

        var events = ParseSseEvents(body);

        var runFinished = events.Last(e => e.GetProperty("type").GetString() == "RUN_FINISHED");
        Assert.True(runFinished.TryGetProperty("metadata", out var metadata));
        Assert.True(metadata.TryGetProperty("usage", out var usage));
        Assert.Equal(100, usage.GetProperty("inputTokens").GetInt32());
        Assert.Equal(50, usage.GetProperty("outputTokens").GetInt32());
        Assert.Equal(150, usage.GetProperty("totalTokens").GetInt32());
    }

    // --------------------------------------------------------------------
    //  Regression tests for bugs we hit live
    // --------------------------------------------------------------------

    /// <summary>
    /// Microsoft.Extensions.AI exposes streaming usage via UsageContent in
    /// ChatResponseUpdate.Contents — the writer must read that path, not just
    /// AdditionalProperties. Symptom when this regressed: Session Cost stuck at $0.
    /// </summary>
    [Fact]
    public async Task ReadsUsageFromUsageContent_NotJustAdditionalProperties()
    {
        var (ctx, body) = CreateMockHttpResponse();

        async IAsyncEnumerable<ChatResponseUpdate> Stream()
        {
            yield return TextUpdate("Hi");
            var usageUpdate = new ChatResponseUpdate();
            usageUpdate.Contents.Add(new UsageContent(new UsageDetails
            {
                InputTokenCount = 42,
                OutputTokenCount = 17
            }));
            yield return usageUpdate;
            await Task.CompletedTask;
        }

        await AgUiSseWriter.WriteStreamAsync(ctx.Response, "t1", "r1", "SolutionArchitect", Stream(), CancellationToken.None);

        var events = ParseSseEvents(body);
        var runFinished = events.Last(e => e.GetProperty("type").GetString() == "RUN_FINISHED");
        Assert.True(runFinished.TryGetProperty("metadata", out var metadata));
        Assert.True(metadata.TryGetProperty("usage", out var usage));
        Assert.Equal(42, usage.GetProperty("inputTokens").GetInt32());
        Assert.Equal(17, usage.GetProperty("outputTokens").GetInt32());
    }

    /// <summary>
    /// SuggestReplies is a UX-only tool — its arguments must be translated into a
    /// STATE_DELTA on /suggestedReplies so the frontend can render reply chips.
    /// Without this, the agent's call would just look like a normal tool call
    /// with no observable effect.
    /// </summary>
    [Fact]
    public async Task SuggestReplies_EmitsStateDelta_WithSuggestionsArray()
    {
        var (ctx, body) = CreateMockHttpResponse();

        async IAsyncEnumerable<ChatResponseUpdate> Stream()
        {
            yield return TextUpdate("Should I create the table now?");
            var toolUpdate = new ChatResponseUpdate();
            toolUpdate.Contents.Add(new FunctionCallContent(
                "call_sr",
                "SuggestReplies",
                new Dictionary<string, object?>
                {
                    ["suggestions"] = new[] { "Yes, create it", "Show me the plan first", "Add fields first" }
                }));
            yield return toolUpdate;
            await Task.CompletedTask;
        }

        await AgUiSseWriter.WriteStreamAsync(ctx.Response, "t1", "r1", "BusinessAnalyst", Stream(), CancellationToken.None);

        var events = ParseSseEvents(body);
        var suggestedDelta = events.LastOrDefault(e =>
            e.GetProperty("type").GetString() == "STATE_DELTA" &&
            e.TryGetProperty("delta", out var d) &&
            d.ValueKind == JsonValueKind.Array &&
            FindPatchValue(d, "/suggestedReplies") is { ValueKind: JsonValueKind.Array } arr &&
            arr.GetArrayLength() > 0);

        Assert.NotEqual(default, suggestedDelta);
        var values = FindPatchValue(suggestedDelta.GetProperty("delta"), "/suggestedReplies")!.Value
            .EnumerateArray().Select(v => v.GetString()).ToArray();
        Assert.Equal(3, values.Length);
        Assert.Equal("Yes, create it", values[0]);
        Assert.Equal("Show me the plan first", values[1]);
        Assert.Equal("Add fields first", values[2]);
    }

    /// <summary>
    /// At RUN_STARTED, an empty-array STATE_DELTA on /suggestedReplies must be
    /// emitted so stale chips from the previous turn don't bleed into the new one.
    /// </summary>
    [Fact]
    public async Task RunStarted_ClearsSuggestedRepliesWithEmptyArray()
    {
        var (ctx, body) = CreateMockHttpResponse();
        var stream = CreateStream("Hello");

        await AgUiSseWriter.WriteStreamAsync(ctx.Response, "t1", "r1", "SolutionArchitect", stream, CancellationToken.None);

        var events = ParseSseEvents(body);
        // Find the early-run reset (should appear right after activeAgent).
        var earlyReset = events
            .TakeWhile(e => e.GetProperty("type").GetString() != "TEXT_MESSAGE_START")
            .FirstOrDefault(e =>
                e.GetProperty("type").GetString() == "STATE_DELTA" &&
                e.TryGetProperty("delta", out var d) &&
                d.ValueKind == JsonValueKind.Array &&
                FindPatchValue(d, "/suggestedReplies") is { ValueKind: JsonValueKind.Array });

        Assert.NotEqual(default, earlyReset);
        var arr = FindPatchValue(earlyReset.GetProperty("delta"), "/suggestedReplies")!.Value;
        Assert.Equal(0, arr.GetArrayLength());
    }

    /// <summary>
    /// TOOL_CALL_START.agentName must reflect the CURRENT agent (which follows
    /// chained handoffs), not the agent name the writer was invoked with.
    /// Symptom when regressed: Tester missing from Agent Timeline; tool calls
    /// rendered under DMB's bubble.
    /// </summary>
    [Fact]
    public async Task ToolCallStart_UsesCurrentAgent_AfterAgentChange()
    {
        var (ctx, body) = CreateMockHttpResponse();

        async IAsyncEnumerable<ChatResponseUpdate> Stream()
        {
            // First agent emits text, then a synthetic agent-name change to "Tester",
            // then a tool call (which should be tagged "Tester", not "DataModelBuilder").
            yield return TextUpdate("DMB working...");
            yield return new ChatResponseUpdate
            {
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["forge_agent_name"] = "Tester"
                }
            };
            var toolUpdate = new ChatResponseUpdate();
            toolUpdate.Contents.Add(new FunctionCallContent("call_x", "verify_table_against_plan", new Dictionary<string, object?>()));
            yield return toolUpdate;
            await Task.CompletedTask;
        }

        await AgUiSseWriter.WriteStreamAsync(ctx.Response, "t1", "r1", "DataModelBuilder", Stream(), CancellationToken.None);

        var events = ParseSseEvents(body);
        var toolStart = events.First(e => e.GetProperty("type").GetString() == "TOOL_CALL_START");
        Assert.True(toolStart.TryGetProperty("agentName", out var agent));
        Assert.Equal("Tester", agent.GetString());
    }

    /// <summary>
    /// When the active agent changes mid-stream, the writer must close the prior
    /// bubble (TEXT_MESSAGE_END) and start a fresh one for the new agent's text.
    /// Symptom when regressed: chained agents' text appended to the previous
    /// agent's bubble, no per-agent attribution in the chat.
    /// </summary>
    [Fact]
    public async Task AgentChangeMidStream_ClosesPriorBubble_AndOpensNew()
    {
        var (ctx, body) = CreateMockHttpResponse();

        async IAsyncEnumerable<ChatResponseUpdate> Stream()
        {
            yield return TextUpdate("First agent says hi to fill the buffer past 20 chars.");
            yield return new ChatResponseUpdate
            {
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["forge_agent_name"] = "Tester"
                }
            };
            yield return TextUpdate("Second agent's text after the handoff is also long.");
            await Task.CompletedTask;
        }

        await AgUiSseWriter.WriteStreamAsync(ctx.Response, "t1", "r1", "DataModelBuilder", Stream(), CancellationToken.None);

        var events = ParseSseEvents(body);
        var starts = events.Where(e => e.GetProperty("type").GetString() == "TEXT_MESSAGE_START").ToList();
        var ends = events.Where(e => e.GetProperty("type").GetString() == "TEXT_MESSAGE_END").ToList();

        // Two distinct bubbles, each with its own START/END.
        Assert.Equal(2, starts.Count);
        Assert.Equal(2, ends.Count);

        // First bubble's agent attribution = DataModelBuilder; second = Tester.
        Assert.Equal("DataModelBuilder", starts[0].GetProperty("agentName").GetString());
        Assert.Equal("Tester", starts[1].GetProperty("agentName").GetString());

        // The two messages must have different ids.
        Assert.NotEqual(
            starts[0].GetProperty("messageId").GetString(),
            starts[1].GetProperty("messageId").GetString());
    }

    /// <summary>
    /// If the model emits ONLY a think block (no body text) followed by a tool call,
    /// the writer should NOT open a TEXT_MESSAGE bubble (otherwise we get an empty
    /// bubble in the chat). Symptom when regressed: "Hello" → empty assistant bubble.
    /// </summary>
    [Fact]
    public async Task ThinkOnly_NoBodyText_NoTextMessageBubble()
    {
        var (ctx, body) = CreateMockHttpResponse();
        // Model emits only the think block (split across chunks). No body text.
        var stream = CreateStream("<think>I'll just call the tool.</think>");

        await AgUiSseWriter.WriteStreamAsync(ctx.Response, "t1", "r1", "SolutionArchitect", stream, CancellationToken.None);

        var events = ParseSseEvents(body);
        var types = events.Select(e => e.GetProperty("type").GetString()).ToList();

        Assert.DoesNotContain("TEXT_MESSAGE_START", types);
        Assert.DoesNotContain("TEXT_MESSAGE_END", types);
        // But the reasoning STATE_DELTA should still be emitted so the user sees the trace.
        var reasoningDelta = events.FirstOrDefault(e =>
            e.GetProperty("type").GetString() == "STATE_DELTA" &&
            e.TryGetProperty("delta", out var d) &&
            d.ValueKind == JsonValueKind.Array &&
            FindPatchValue(d, "/reasoning") != null);
        Assert.NotEqual(default, reasoningDelta);
    }

    /// <summary>
    /// Defensive: if the model emits a never-closed think tag, the buffered text
    /// must still flush at end of stream so we don't silently drop content.
    /// </summary>
    [Fact]
    public async Task UnclosedThinkTag_FlushesAtEndOfStream()
    {
        var (ctx, body) = CreateMockHttpResponse();
        // No closing </think> — buffer keeps growing past threshold and then stream ends.
        var stream = CreateStream("<think>Reasoning that was cut off mid-flow forever and ever long enough");

        await AgUiSseWriter.WriteStreamAsync(ctx.Response, "t1", "r1", "SolutionArchitect", stream, CancellationToken.None);

        var events = ParseSseEvents(body);
        var types = events.Select(e => e.GetProperty("type").GetString()).ToList();
        // The buffered content should be flushed as TEXT_MESSAGE_CONTENT, not lost.
        Assert.Contains("TEXT_MESSAGE_START", types);
        Assert.Contains("TEXT_MESSAGE_END", types);
    }
}
