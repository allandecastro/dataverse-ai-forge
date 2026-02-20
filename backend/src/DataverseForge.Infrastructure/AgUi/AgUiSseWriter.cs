using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;

namespace DataverseForge.Infrastructure.AgUi;

/// <summary>
/// Converts IAsyncEnumerable&lt;ChatResponseUpdate&gt; to AG-UI SSE events
/// written to an HttpResponse.
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
        IAsyncEnumerable<ChatResponseUpdate> stream,
        CancellationToken cancellationToken)
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

        var messageId = Guid.NewGuid().ToString("N");
        var textStarted = false;
        var activeFunctionCalls = new Dictionary<string, bool>();

        await foreach (var update in stream.WithCancellation(cancellationToken))
        {
            // Handle text content
            if (update.Text is { Length: > 0 } text)
            {
                if (!textStarted)
                {
                    await WriteEventAsync(response, new
                    {
                        type = "TEXT_MESSAGE_START",
                        threadId,
                        runId,
                        messageId,
                        role = "assistant"
                    }, cancellationToken);
                    textStarted = true;
                }

                await WriteEventAsync(response, new
                {
                    type = "TEXT_MESSAGE_CONTENT",
                    threadId,
                    runId,
                    messageId,
                    delta = text
                }, cancellationToken);
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
                                toolCallName = functionCall.Name
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

        await WriteEventAsync(response, new
        {
            type = "RUN_FINISHED",
            threadId,
            runId
        }, cancellationToken);

        await response.Body.FlushAsync(cancellationToken);
    }

    private static async Task WriteEventAsync(HttpResponse response, object data, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes($"data: {json}\n\n");
        await response.Body.WriteAsync(bytes, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
