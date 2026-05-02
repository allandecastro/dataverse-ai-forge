using System.Text;

namespace DataverseForge.Infrastructure.AgUi.Internal;

/// <summary>
/// State machine that consumes streaming text chunks and separates &lt;think&gt;...&lt;/think&gt;
/// reasoning blocks from user-visible body text. Pure logic — no I/O. The caller
/// emits SSE events based on what each <see cref="Ingest"/> returns.
///
/// Behavior:
///   - First chunks accumulate in a buffer until we either see "&lt;think" or hit the
///     detection threshold without it.
///   - If "&lt;think" appears, we keep buffering until "&lt;/think&gt;" closes it. On close,
///     the buffered reasoning is returned and any text after &lt;/think&gt; is yielded as
///     plain content. The state then transitions to <see cref="State.Plain"/>.
///   - If the threshold is hit without "&lt;think", we flush the buffer as plain text and
///     transition to <see cref="State.Plain"/>.
///   - In <see cref="State.Plain"/>, every chunk passes through as plain content.
///
/// Past regressions this guards:
///   - Empty bubble when model emits only &lt;think&gt;...&lt;/think&gt; → AfterThink is empty,
///     caller must NOT open a bubble. (Caller checks <see cref="IngestResult.PlainText"/>.)
///   - Unclosed &lt;think&gt; tag at end of stream → caller's Flush() emits the raw buffer
///     so content isn't silently dropped.
/// </summary>
internal sealed class ThinkTagParser
{
    public const int DefaultDetectionThreshold = 20;

    private enum State { Detecting, BufferingThink, Plain }

    private readonly StringBuilder _buffer = new();
    private readonly int _detectionThreshold;
    private State _state = State.Detecting;

    public ThinkTagParser(int detectionThreshold = DefaultDetectionThreshold)
    {
        _detectionThreshold = detectionThreshold;
    }

    /// <summary>True once a complete &lt;think&gt;...&lt;/think&gt; has been parsed (or no think
    /// was detected after the threshold). Subsequent text streams as plain content.</summary>
    public bool ThinkComplete => _state == State.Plain;

    /// <summary>True if any in-flight buffer content is held — caller's end-of-stream
    /// flush should drain it.</summary>
    public bool HasBuffered => _buffer.Length > 0;

    public IngestResult Ingest(string chunk)
    {
        if (_state == State.Plain)
        {
            return new IngestResult(reasoning: null, plainText: chunk);
        }

        _buffer.Append(chunk);
        var s = _buffer.ToString();

        if (_state == State.Detecting)
        {
            if (s.Contains("<think", StringComparison.Ordinal))
            {
                _state = State.BufferingThink;
            }
            else if (_buffer.Length >= _detectionThreshold)
            {
                // No <think tag — flush as plain text.
                _state = State.Plain;
                _buffer.Clear();
                return new IngestResult(reasoning: null, plainText: s);
            }
            else
            {
                // Still under threshold, no tag yet, hold.
                return IngestResult.Empty;
            }
        }

        // BufferingThink — wait for </think>.
        var endIdx = s.IndexOf("</think>", StringComparison.Ordinal);
        if (endIdx < 0)
        {
            return IngestResult.Empty;
        }

        // We treat any "<think" detection as the start of the open tag and skip
        // until '>' to be permissive about variants like "<think foo>". Most models
        // emit "<think>" verbatim; the search-by-'>' is a defensive belt.
        var startIdx = s.IndexOf("<think", StringComparison.Ordinal);
        var openTagClose = s.IndexOf('>', startIdx);
        if (openTagClose < 0 || openTagClose >= endIdx)
        {
            // Malformed — treat the open tag as exactly "<think>".
            startIdx = s.IndexOf("<think>", StringComparison.Ordinal);
            openTagClose = startIdx + "<think>".Length - 1;
            if (startIdx < 0)
            {
                // No proper open tag — treat the entire span as reasoning.
                return new IngestResult(reasoning: s[..endIdx].Trim(), plainText: null);
            }
        }
        var bodyStart = openTagClose + 1;
        var reasoning = s.Substring(bodyStart, endIdx - bodyStart).Trim();
        var afterThink = s[(endIdx + "</think>".Length)..].TrimStart();

        _state = State.Plain;
        _buffer.Clear();
        return new IngestResult(
            reasoning: reasoning,
            plainText: afterThink.Length > 0 ? afterThink : null);
    }

    /// <summary>Drain any remaining buffer at end-of-stream. Returns the raw text so the
    /// caller can decide whether to emit it as content or discard.</summary>
    public string? Flush()
    {
        if (_buffer.Length == 0) return null;
        var text = _buffer.ToString();
        _buffer.Clear();
        return text;
    }

    /// <summary>Reset to initial state — used when the active agent changes mid-stream
    /// and the next agent's response should be parsed independently.</summary>
    public void Reset()
    {
        _buffer.Clear();
        _state = State.Detecting;
    }

    public readonly record struct IngestResult(string? reasoning, string? plainText)
    {
        public static IngestResult Empty => new(null, null);
        public bool HasOutput => reasoning is not null || plainText is not null;
    }
}
