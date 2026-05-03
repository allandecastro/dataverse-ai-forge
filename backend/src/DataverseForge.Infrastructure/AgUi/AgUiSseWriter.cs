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

        // Fallback chain when the agent didn't call SuggestReplies:
        //   1. Try inline `[suggest_replies]\n- A\n- B` markup.
        //   2. Otherwise, if the bubble ends in a yes/no-shaped question, synthesize
        //      default Yes / No chips so the user always has at least one click.
        // Skip both when the tool was actually called — we'd clobber real chips.
        if (!tools.SuggestRepliesEmitted)
        {
            var fallbackChips = ExtractInlineSuggestReplies(bubble.BufferedText);
            if (fallbackChips.Count == 0)
            {
                fallbackChips = SynthesizeYesNoChips(bubble.BufferedText);
            }
            if (fallbackChips.Count > 0)
            {
                await WriteStateDeltaAsync(response, threadId, runId, "/suggestedReplies", fallbackChips.ToArray(), cancellationToken);
            }
        }

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

    /// <summary>
    /// Markers that introduce an inline suggestion list (case-insensitive,
    /// substring match). Agents have been observed emitting all of these
    /// variants instead of calling the SuggestReplies tool. Order doesn't
    /// matter — the parser uses the EARLIEST match in the text.
    /// </summary>
    private static readonly string[] SuggestRepliesMarkers =
    [
        "[suggest_replies]",
        "suggested replies:",
        "suggested reply:",
        "suggest your next step:",
        "suggested next steps:",
        "suggested next step:",
    ];

    /// <summary>
    /// Last-resort fallback: when the agent's bubble ends in a yes/no-shaped
    /// question (model didn't call SuggestReplies and didn't emit `[suggest_replies]`
    /// markup either), synthesize default Yes / No chips so the user always has
    /// at least a one-click reply path. We only fire on a tight set of
    /// well-known question stems to avoid over-triggering on questions that
    /// take free-form input ("which fields?", "what name?").
    ///
    /// Patterns matched (case-insensitive, all anchored to the LAST sentence):
    ///   - "Shall I X?"            → Yes / No
    ///   - "Should I X?"           → Yes / No
    ///   - "Should we X?"          → Yes / No
    ///   - "Would you like (me to|to) X?"  → Yes / No
    ///   - "Do you want (me to)? X?"  → Yes / No
    ///   - "Can I X?" / "May I X?" → Yes / No
    ///   - "Anything else?" / "Any other...?"  → "No, that's all" / "Yes, more"
    /// </summary>
    internal static List<string> SynthesizeYesNoChips(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        // Look at the trailing portion of the bubble — the question is almost
        // always the last sentence. Trim trailing whitespace/asterisks/markdown.
        var trimmed = text.TrimEnd().TrimEnd('*', '_', ' ', '\n', '\r');
        if (!trimmed.EndsWith('?')) return [];

        // Pull last ~200 chars to keep the regex bounded and avoid matching
        // earlier sentences in a long bubble.
        var tail = trimmed.Length <= 200 ? trimmed : trimmed.Substring(trimmed.Length - 200);
        var lower = tail.ToLowerInvariant();

        // "Anything else" family → broader chips.
        if (lower.Contains("anything else") || lower.Contains("any other"))
        {
            return ["No, that's all", "Yes, more changes"];
        }

        // Yes/No-shaped stems. The regex matches the BEGINNING of the question
        // (after the last sentence boundary) so we don't false-positive on
        // questions like "Where shall I put it?" — that doesn't start with
        // "Shall I", it starts with "Where".
        // Find the start of the last sentence:
        var lastSentenceStart = 0;
        for (var i = lower.Length - 2; i >= 0; i--)
        {
            var c = lower[i];
            if (c == '.' || c == '!' || c == '?' || c == '\n')
            {
                lastSentenceStart = i + 1;
                break;
            }
        }
        var lastSentence = lower.Substring(lastSentenceStart).TrimStart();
        var stems = new[]
        {
            "shall i ", "shall we ", "should i ", "should we ",
            "would you like ", "do you want ", "do you wish ",
            "can i ", "may i ", "may we ",
        };
        foreach (var stem in stems)
        {
            if (lastSentence.StartsWith(stem, StringComparison.Ordinal))
            {
                return ["Yes", "No"];
            }
        }
        return [];
    }

    /// <summary>
    /// Parses inline suggestion markup out of the agent's text. Recognized shapes:
    ///   `[suggest_replies]\n- A\n- B`
    ///   `Suggested replies:\n- A\n- B`
    ///   `Suggest your next step:\n- A\n- B`
    /// The parser scans for the EARLIEST occurrence of any known marker,
    /// then collects subsequent bullet lines (`- text` or `* text`) until a
    /// non-bullet line or end of text. Returns at most 4 trimmed, non-empty
    /// suggestions; empty list if nothing matched.
    /// </summary>
    internal static List<string> ExtractInlineSuggestReplies(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        // Find the earliest marker that appears in the text.
        var earliestIdx = -1;
        var earliestLength = 0;
        foreach (var marker in SuggestRepliesMarkers)
        {
            var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && (earliestIdx < 0 || idx < earliestIdx))
            {
                earliestIdx = idx;
                earliestLength = marker.Length;
            }
        }
        if (earliestIdx < 0) return [];

        var afterMarker = text.Substring(earliestIdx + earliestLength);
        var lines = afterMarker.Split('\n');
        var chips = new List<string>();
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue; // tolerate blank lines between marker and list
            // Bullet line: "- text" or "* text"
            if ((line[0] == '-' || line[0] == '*') && line.Length > 1)
            {
                var chip = line.Substring(1).Trim();
                if (chip.Length > 0)
                {
                    chips.Add(chip);
                    if (chips.Count >= 4) break;
                    continue;
                }
            }
            // Non-bullet line after we already collected at least one chip → end
            // of the suggestion block. If we haven't collected any yet, keep
            // scanning — the agent occasionally emits whitespace or filler before
            // the first bullet.
            if (chips.Count > 0) break;
        }
        return chips;
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
