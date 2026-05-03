using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;

namespace DataverseForge.Infrastructure.AgUi.Internal;

/// <summary>
/// Emits TOOL_CALL_START / TOOL_CALL_ARGS / TOOL_CALL_RESULT for FunctionCallContent
/// and FunctionResultContent items. De-duplicates start events per call id, and tags
/// each TOOL_CALL_START with the CURRENT agent (which follows chained handoffs) so
/// the frontend can attribute tools correctly.
///
/// Past regression: tagging with the outer agentName (frozen at turn start) made
/// Tester invisible in the Agent Timeline because all of its tools were labeled
/// DataModelBuilder. The current-agent parameter on Emit is the fix.
/// </summary>
internal sealed class FunctionCallEmitter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpResponse _response;
    private readonly string _threadId;
    private readonly string _runId;
    private readonly Func<HttpResponse, object, CancellationToken, Task> _writeEvent;
    private readonly HashSet<string> _emittedStarts = [];
    private bool _suggestRepliesEmitted;

    /// <summary>True once a SuggestReplies tool call has been translated into
    /// a /suggestedReplies STATE_DELTA. Lets the writer know whether to fall
    /// back to scraping the agent's text for inlined `[suggest_replies]` markup.</summary>
    public bool SuggestRepliesEmitted => _suggestRepliesEmitted;

    public FunctionCallEmitter(
        HttpResponse response,
        string threadId,
        string runId,
        Func<HttpResponse, object, CancellationToken, Task> writeEvent)
    {
        _response = response;
        _threadId = threadId;
        _runId = runId;
        _writeEvent = writeEvent;
    }

    public async Task EmitFromUpdateAsync(
        ChatResponseUpdate update, string currentAgent, CancellationToken ct)
    {
        if (update.Contents == null) return;

        foreach (var content in update.Contents)
        {
            if (content is FunctionCallContent fc)
            {
                var callId = fc.CallId ?? Guid.NewGuid().ToString("N");
                if (_emittedStarts.Add(callId))
                {
                    await _writeEvent(_response, new
                    {
                        type = "TOOL_CALL_START",
                        threadId = _threadId,
                        runId = _runId,
                        toolCallId = callId,
                        toolCallName = fc.Name,
                        agentName = currentAgent,
                    }, ct);
                }
                if (fc.Arguments != null)
                {
                    var argsJson = JsonSerializer.Serialize(fc.Arguments, JsonOptions);
                    await _writeEvent(_response, new
                    {
                        type = "TOOL_CALL_ARGS",
                        threadId = _threadId,
                        runId = _runId,
                        toolCallId = callId,
                        delta = argsJson,
                    }, ct);

                    // SuggestReplies is a UX-only tool: its sole purpose is to
                    // surface clickable reply chips. Translate its arguments
                    // into a STATE_DELTA so the frontend can render them
                    // without the agent needing a separate channel.
                    if (string.Equals(fc.Name, "SuggestReplies", StringComparison.Ordinal))
                    {
                        await EmitSuggestedRepliesDeltaAsync(fc.Arguments, ct);
                    }
                }
                // Match original: emit TOOL_CALL_END after every function-call content
                // (the SDK chunks args across updates, but each chunk represents one
                // semantic call — the frontend treats END as "args complete enough to render").
                await _writeEvent(_response, new
                {
                    type = "TOOL_CALL_END",
                    threadId = _threadId,
                    runId = _runId,
                    toolCallId = callId,
                }, ct);
            }
            else if (content is FunctionResultContent fr)
            {
                var resultCallId = fr.CallId ?? "unknown";
                var resultJson = fr.Result != null
                    ? JsonSerializer.Serialize(fr.Result, JsonOptions)
                    : "null";
                await _writeEvent(_response, new
                {
                    type = "TOOL_CALL_RESULT",
                    threadId = _threadId,
                    runId = _runId,
                    toolCallId = resultCallId,
                    result = resultJson,
                }, ct);
            }
        }
    }

    /// <summary>
    /// Pulls the `suggestions` array from SuggestReplies' arguments and emits a
    /// STATE_DELTA on /suggestedReplies. The agent's argument shape can be either
    /// a JSON array or a stringified JSON array (the SDK has been observed
    /// producing both); we tolerate either.
    /// </summary>
    private async Task EmitSuggestedRepliesDeltaAsync(
        IDictionary<string, object?> arguments, CancellationToken ct)
    {
        if (!arguments.TryGetValue("suggestions", out var raw) || raw is null) return;

        List<string>? items = null;

        switch (raw)
        {
            case IEnumerable<string> stringList:
                items = stringList.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                break;
            case System.Collections.IEnumerable collection when raw is not string:
                items = [];
                foreach (var item in collection)
                {
                    if (item is string s && !string.IsNullOrWhiteSpace(s)) items.Add(s);
                    else if (item is JsonElement je && je.ValueKind == JsonValueKind.String && je.GetString() is { Length: > 0 } v) items.Add(v);
                }
                break;
            case JsonElement je when je.ValueKind == JsonValueKind.Array:
                items = [];
                foreach (var el in je.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 } v) items.Add(v);
                }
                break;
            case string serialized:
                try
                {
                    var parsed = JsonSerializer.Deserialize<string[]>(serialized);
                    if (parsed != null)
                        items = parsed.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                }
                catch (JsonException)
                {
                    // Fall through — agent passed a malformed string.
                }
                break;
        }

        if (items is null || items.Count == 0) return;

        // Cap defensively at 4 — the prompt asks for 2-4 but we shouldn't trust
        // an unbounded LLM output to honor that.
        if (items.Count > 4) items = [.. items.Take(4)];

        await _writeEvent(_response, new
        {
            type = "STATE_DELTA",
            threadId = _threadId,
            runId = _runId,
            delta = new object[]
            {
                new { op = "replace", path = "/suggestedReplies", value = items.ToArray() }
            }
        }, ct);
        _suggestRepliesEmitted = true;
    }
}
