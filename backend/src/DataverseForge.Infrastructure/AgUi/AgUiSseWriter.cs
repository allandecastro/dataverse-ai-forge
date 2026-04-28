using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;

namespace DataverseForge.Infrastructure.AgUi;

/// <summary>
/// Converts IAsyncEnumerable&lt;ChatResponseUpdate&gt; to AG-UI SSE events
/// written to an HttpResponse. Handles &lt;think&gt; tag buffering,
/// STATE_DELTA emission for active agent + reasoning, and usage metadata.
/// </summary>
public static class AgUiSseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Buffer threshold: if we accumulate this many chars without seeing
    /// "&lt;think", we flush the buffer as normal text (no think tags present).
    /// </summary>
    private const int ThinkDetectionThreshold = 20;

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

        await WriteEventAsync(response, new
        {
            type = "RUN_STARTED",
            threadId,
            runId
        }, cancellationToken);

        // Emit STATE_DELTA with activeAgent (JSON Patch format)
        if (!string.IsNullOrEmpty(agentName))
        {
            await WriteEventAsync(response, new
            {
                type = "STATE_DELTA",
                threadId,
                runId,
                delta = new object[]
                {
                    new { op = "replace", path = "/activeAgent", value = agentName }
                }
            }, cancellationToken);
        }

        // Emit STATE_DELTA with solution state (JSON Patch format)
        if (solutionState != null)
        {
            await WriteEventAsync(response, new
            {
                type = "STATE_DELTA",
                threadId,
                runId,
                delta = new object[]
                {
                    new { op = "replace", path = "/solutionState", value = solutionState }
                }
            }, cancellationToken);
        }

        var messageId = Guid.NewGuid().ToString("N");
        var textStarted = false;
        var activeFunctionCalls = new Dictionary<string, bool>();

        // Think-tag buffering state
        var textBuffer = new StringBuilder();
        var thinkDetected = false;
        var thinkComplete = false;
        int? inputTokens = null;
        int? outputTokens = null;

        await foreach (var update in stream.WithCancellation(cancellationToken))
        {
            // Extract agent name from synthetic metadata update (skip processing)
            if (update.AdditionalProperties?.TryGetValue("forge_agent_name", out var _) == true)
            {
                continue;
            }

            // Track usage from AdditionalProperties (if provider exposes it)
            if (update.AdditionalProperties != null)
            {
                if (update.AdditionalProperties.TryGetValue("inputTokens", out var inp) && inp is int inpVal)
                    inputTokens = inpVal;
                if (update.AdditionalProperties.TryGetValue("outputTokens", out var outp) && outp is int outpVal)
                    outputTokens = outpVal;
            }

            // Handle text content with think-tag buffering
            if (update.Text is { Length: > 0 } text)
            {
                if (!thinkComplete)
                {
                    // Still looking for think tags
                    textBuffer.Append(text);
                    var bufferStr = textBuffer.ToString();

                    if (!thinkDetected && bufferStr.Contains("<think", StringComparison.Ordinal))
                    {
                        thinkDetected = true;
                    }

                    if (thinkDetected && bufferStr.Contains("</think>", StringComparison.Ordinal))
                    {
                        // Extract reasoning and remaining text
                        var startIdx = bufferStr.IndexOf("<think>", StringComparison.Ordinal);
                        var endIdx = bufferStr.IndexOf("</think>", StringComparison.Ordinal);

                        if (startIdx >= 0 && endIdx > startIdx)
                        {
                            var reasoning = bufferStr.Substring(startIdx + 7, endIdx - startIdx - 7).Trim();

                            // Emit STATE_DELTA with reasoning (JSON Patch format)
                            await WriteEventAsync(response, new
                            {
                                type = "STATE_DELTA",
                                threadId,
                                runId,
                                delta = new object[]
                                {
                                    new { op = "replace", path = "/reasoning", value = reasoning }
                                }
                            }, cancellationToken);

                            // Stream any text after </think> as normal content
                            var afterThink = bufferStr[(endIdx + 8)..].TrimStart();
                            if (afterThink.Length > 0)
                            {
                                if (!textStarted)
                                {
                                    await WriteTextMessageStartAsync(response, threadId, runId, messageId, cancellationToken);
                                    textStarted = true;
                                }
                                await WriteTextContentAsync(response, threadId, runId, messageId, afterThink, cancellationToken);
                            }
                        }

                        thinkComplete = true;
                        textBuffer.Clear();
                    }
                    else if (!thinkDetected && bufferStr.Length >= ThinkDetectionThreshold)
                    {
                        // No think tags — flush buffer as normal text
                        thinkComplete = true;
                        if (!textStarted)
                        {
                            await WriteTextMessageStartAsync(response, threadId, runId, messageId, cancellationToken);
                            textStarted = true;
                        }
                        await WriteTextContentAsync(response, threadId, runId, messageId, bufferStr, cancellationToken);
                        textBuffer.Clear();
                    }
                }
                else
                {
                    // Think detection phase is over — stream text normally
                    if (!textStarted)
                    {
                        await WriteTextMessageStartAsync(response, threadId, runId, messageId, cancellationToken);
                        textStarted = true;
                    }
                    await WriteTextContentAsync(response, threadId, runId, messageId, text, cancellationToken);
                }
            }

            // Handle tool/function calls
            if (update.Contents != null)
            {
                foreach (var content in update.Contents)
                {
                    if (content is FunctionCallContent functionCall)
                    {
                        var callId = functionCall.CallId ?? Guid.NewGuid().ToString("N");

                        if (!activeFunctionCalls.ContainsKey(callId))
                        {
                            activeFunctionCalls[callId] = true;

                            await WriteEventAsync(response, new
                            {
                                type = "TOOL_CALL_START",
                                threadId,
                                runId,
                                toolCallId = callId,
                                toolCallName = functionCall.Name,
                                agentName
                            }, cancellationToken);
                        }

                        if (functionCall.Arguments != null)
                        {
                            var argsJson = JsonSerializer.Serialize(functionCall.Arguments, JsonOptions);
                            await WriteEventAsync(response, new
                            {
                                type = "TOOL_CALL_ARGS",
                                threadId,
                                runId,
                                toolCallId = callId,
                                delta = argsJson
                            }, cancellationToken);
                        }

                        await WriteEventAsync(response, new
                        {
                            type = "TOOL_CALL_END",
                            threadId,
                            runId,
                            toolCallId = callId
                        }, cancellationToken);
                    }

                    if (content is FunctionResultContent functionResult)
                    {
                        var resultCallId = functionResult.CallId ?? "unknown";
                        var resultJson = functionResult.Result != null
                            ? JsonSerializer.Serialize(functionResult.Result, JsonOptions)
                            : "null";

                        await WriteEventAsync(response, new
                        {
                            type = "TOOL_CALL_RESULT",
                            threadId,
                            runId,
                            toolCallId = resultCallId,
                            result = resultJson
                        }, cancellationToken);
                    }
                }
            }
        }

        // Flush any remaining buffer (e.g. very short messages with no think tags)
        if (textBuffer.Length > 0)
        {
            if (!textStarted)
            {
                await WriteTextMessageStartAsync(response, threadId, runId, messageId, cancellationToken);
                textStarted = true;
            }
            await WriteTextContentAsync(response, threadId, runId, messageId, textBuffer.ToString(), cancellationToken);
        }

        if (textStarted)
        {
            await WriteEventAsync(response, new
            {
                type = "TEXT_MESSAGE_END",
                threadId,
                runId,
                messageId
            }, cancellationToken);
        }

        // Re-emit STATE_DELTA with the latest solution state — captures any mutations
        // tools made (CreateSolution, SelectSolution, ExecuteCreate*, etc.).
        if (solutionStateRefresher != null)
        {
            try
            {
                var latest = await solutionStateRefresher();
                await WriteEventAsync(response, new
                {
                    type = "STATE_DELTA",
                    threadId,
                    runId,
                    delta = new object[]
                    {
                        new { op = "replace", path = "/solutionState", value = latest }
                    }
                }, cancellationToken);
            }
            catch
            {
                // Best-effort — never fail the run because of a refresh
            }
        }

        // Build RUN_FINISHED with optional usage metadata
        if (inputTokens.HasValue || outputTokens.HasValue)
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
                        inputTokens = inputTokens ?? 0,
                        outputTokens = outputTokens ?? 0,
                        totalTokens = (inputTokens ?? 0) + (outputTokens ?? 0)
                    }
                }
            }, cancellationToken);
        }
        else
        {
            await WriteEventAsync(response, new
            {
                type = "RUN_FINISHED",
                threadId,
                runId
            }, cancellationToken);
        }

        await response.Body.FlushAsync(cancellationToken);
    }

    private static async Task WriteTextMessageStartAsync(
        HttpResponse response, string threadId, string runId, string messageId, CancellationToken ct)
    {
        await WriteEventAsync(response, new
        {
            type = "TEXT_MESSAGE_START",
            threadId,
            runId,
            messageId,
            role = "assistant"
        }, ct);
    }

    private static async Task WriteTextContentAsync(
        HttpResponse response, string threadId, string runId, string messageId, string delta, CancellationToken ct)
    {
        await WriteEventAsync(response, new
        {
            type = "TEXT_MESSAGE_CONTENT",
            threadId,
            runId,
            messageId,
            delta
        }, ct);
    }

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

        var messageId = Guid.NewGuid().ToString("N");

        await WriteEventAsync(response, new { type = "RUN_STARTED", threadId, runId }, cancellationToken);
        await WriteTextMessageStartAsync(response, threadId, runId, messageId, cancellationToken);
        await WriteTextContentAsync(response, threadId, runId, messageId, errorMessage, cancellationToken);
        await WriteEventAsync(response, new { type = "TEXT_MESSAGE_END", threadId, runId, messageId }, cancellationToken);
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
