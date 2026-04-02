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
}
