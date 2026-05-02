using System.Text;
using System.Text.Json;
using DataverseForge.Infrastructure.AgUi.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;

namespace DataverseForge.Infrastructure.AgUi;

/// <summary>
/// Converts <see cref="IAsyncEnumerable{ChatResponseUpdate}"/> to AG-UI SSE events
/// written to an <see cref="HttpResponse"/>. The bug-prone bits live in
/// <c>Internal/</c> as focused classes:
///   <see cref="ThinkTagParser"/>     — &lt;think&gt;...&lt;/think&gt; state machine
///   <see cref="AssistantBubble"/>    — TEXT_MESSAGE_* lifecycle (lazy open)
///   <see cref="FunctionCallEmitter"/>— TOOL_CALL_* with current-agent attribution
///   <see cref="UsageReader"/>        — accumulates usage from UsageContent + AdditionalProperties
/// This top-level method orchestrates them and handles SSE framing + STATE_DELTA emission.
/// </summary>
public static class AgUiSseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task WriteStreamAsync(
        HttpResponse response,
        string threadId,
        string runId,
        string agentName,
        IAsyncEnumerable<ChatResponseUpdate> stream,
        CancellationToken cancellationToken,
        object? solutionState = null,
        Func<Task<object>>? solutionStateRefresher = null)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";

        await WriteEventAsync(response, new { type = "RUN_STARTED", threadId, runId }, cancellationToken);

        if (!string.IsNullOrEmpty(agentName))
        {
            await WriteStateDeltaAsync(response, threadId, runId, "/activeAgent", agentName, cancellationToken);
        }
        if (solutionState != null)
        {
            await WriteStateDeltaAsync(response, threadId, runId, "/solutionState", solutionState, cancellationToken);
        }
        // Clear any suggestion chips left over from the previous turn — by the time
        // a new run starts, the user already either clicked or typed something else,
        // so the stale chips no longer apply.
        await WriteStateDeltaAsync(response, threadId, runId, "/suggestedReplies", Array.Empty<string>(), cancellationToken);

        var bubble = new AssistantBubble(response, threadId, runId, agentName, WriteEventAsync);
        var parser = new ThinkTagParser();
        var usage = new UsageReader();
        var tools = new FunctionCallEmitter(response, threadId, runId, WriteEventAsync);
        var currentAgent = agentName;

        await foreach (var update in stream.WithCancellation(cancellationToken))
        {
            // Synthetic agent-name change from the orchestrator (chained handoff).
            // Close the prior bubble + reset the parser so each agent's text gets its
            // own bubble with its own attribution.
            if (update.AdditionalProperties?.TryGetValue("forge_agent_name", out var newAgentObj) == true)
            {
                var newAgent = newAgentObj?.ToString() ?? currentAgent;
                if (!string.Equals(newAgent, currentAgent, StringComparison.Ordinal))
                {
                    await FlushParserResidueAsync(parser, bubble, cancellationToken);
                    await bubble.CloseAsync(cancellationToken);
                    await WriteStateDeltaAsync(response, threadId, runId, "/activeAgent", newAgent, cancellationToken);
                    currentAgent = newAgent;
                    bubble.StartNewBubbleFor(newAgent);
                    parser.Reset();
                }
                continue;
            }

            usage.Ingest(update);

            if (update.Text is { Length: > 0 } text)
            {
                var result = parser.Ingest(text);
                if (result.reasoning is not null)
                {
                    await WriteStateDeltaAsync(response, threadId, runId, "/reasoning", result.reasoning, cancellationToken);
                }
                if (result.plainText is not null)
                {
                    await bubble.WriteContentAsync(result.plainText, cancellationToken);
                }
            }

            await tools.EmitFromUpdateAsync(update, currentAgent, cancellationToken);
        }

        // End-of-stream: drain any remaining buffered text (unclosed think tag, short
        // message under detection threshold, etc.) so we don't silently swallow content.
        await FlushParserResidueAsync(parser, bubble, cancellationToken);
        await bubble.CloseAsync(cancellationToken);

        // Refresh solution state so the sidebar reflects any tool-driven mutations
        // (CreateSolution, ExecuteCreate*, etc.) regardless of which agent ran them.
        if (solutionStateRefresher != null)
        {
            try
            {
                var latest = await solutionStateRefresher();
                await WriteStateDeltaAsync(response, threadId, runId, "/solutionState", latest, cancellationToken);
            }
            catch
            {
                // Best-effort — never fail the run because of a refresh.
            }
        }

        if (usage.HasUsage)
        {
            await WriteEventAsync(response, new
            {
                type = "RUN_FINISHED",
                threadId,
                runId,
                metadata = new
                {
                    usage = new
                    {
                        inputTokens = usage.InputTokens,
                        outputTokens = usage.OutputTokens,
                        totalTokens = usage.TotalTokens,
                    }
                }
            }, cancellationToken);
        }
        else
        {
            await WriteEventAsync(response, new { type = "RUN_FINISHED", threadId, runId }, cancellationToken);
        }

        await response.Body.FlushAsync(cancellationToken);
    }

    private static async Task FlushParserResidueAsync(
        ThinkTagParser parser, AssistantBubble bubble, CancellationToken ct)
    {
        var residue = parser.Flush();
        if (!string.IsNullOrEmpty(residue))
        {
            await bubble.WriteContentAsync(residue, ct);
        }
    }

    private static Task WriteStateDeltaAsync(
        HttpResponse response, string threadId, string runId, string path, object value, CancellationToken ct) =>
        WriteEventAsync(response, new
        {
            type = "STATE_DELTA",
            threadId,
            runId,
            delta = new object[]
            {
                new { op = "replace", path, value }
            }
        }, ct);

    /// <summary>
    /// Write an error message as a complete SSE stream (RUN_STARTED → TEXT → RUN_FINISHED).
    /// Used when errors occur after streaming has started or for configuration errors.
    /// </summary>
    public static async Task WriteErrorAsStreamAsync(
        HttpResponse response,
        string threadId,
        string runId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        if (!response.HasStarted)
        {
            response.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            response.Headers.Connection = "keep-alive";
            response.Headers["X-Accel-Buffering"] = "no";
        }

        await WriteEventAsync(response, new { type = "RUN_STARTED", threadId, runId }, cancellationToken);
        var bubble = new AssistantBubble(response, threadId, runId, "system", WriteEventAsync);
        await bubble.WriteContentAsync(errorMessage, cancellationToken);
        await bubble.CloseAsync(cancellationToken);
        await WriteEventAsync(response, new { type = "RUN_FINISHED", threadId, runId }, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    internal static async Task WriteEventAsync(HttpResponse response, object data, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes($"data: {json}\n\n");
        await response.Body.WriteAsync(bytes, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
