using DataverseForge.Infrastructure.AgUi.Internal;
using Xunit;

namespace DataverseForge.Infrastructure.Tests.AgUi;

public class ThinkTagParserTests
{
    [Fact]
    public void Plain_text_above_threshold_passes_through_as_plain()
    {
        var parser = new ThinkTagParser(detectionThreshold: 5);

        var result = parser.Ingest("hello world this is plain");

        Assert.Null(result.reasoning);
        Assert.Equal("hello world this is plain", result.plainText);
        Assert.True(parser.ThinkComplete);
    }

    [Fact]
    public void Plain_text_below_threshold_buffers_until_threshold()
    {
        var parser = new ThinkTagParser(detectionThreshold: 20);

        var first = parser.Ingest("hi");
        Assert.Null(first.reasoning);
        Assert.Null(first.plainText);
        Assert.False(parser.ThinkComplete);
        Assert.True(parser.HasBuffered);

        // Crossing the threshold flushes the accumulated buffer as plain.
        var second = parser.Ingest(" there everyone in the room");
        Assert.Null(second.reasoning);
        Assert.Equal("hi there everyone in the room", second.plainText);
        Assert.True(parser.ThinkComplete);
    }

    [Fact]
    public void Complete_think_tag_extracts_reasoning_and_yields_following_plain()
    {
        var parser = new ThinkTagParser();

        var result = parser.Ingest("<think>internal thoughts</think>visible body");

        Assert.Equal("internal thoughts", result.reasoning);
        Assert.Equal("visible body", result.plainText);
        Assert.True(parser.ThinkComplete);
    }

    [Fact]
    public void Only_think_block_with_no_body_returns_null_plain_text()
    {
        var parser = new ThinkTagParser();

        var result = parser.Ingest("<think>just thinking</think>");

        Assert.Equal("just thinking", result.reasoning);
        Assert.Null(result.plainText);
        Assert.True(parser.ThinkComplete);
    }

    [Fact]
    public void Streaming_think_chunks_buffer_until_close_then_emit()
    {
        var parser = new ThinkTagParser();

        var chunk1 = parser.Ingest("<think>part one ");
        Assert.False(chunk1.HasOutput);

        var chunk2 = parser.Ingest("part two ");
        Assert.False(chunk2.HasOutput);

        var chunk3 = parser.Ingest("done</think>final answer");
        Assert.Equal("part one part two done", chunk3.reasoning);
        Assert.Equal("final answer", chunk3.plainText);
    }

    [Fact]
    public void Unclosed_think_at_end_of_stream_is_returned_by_Flush()
    {
        var parser = new ThinkTagParser();

        var inflight = parser.Ingest("<think>never closed reasoning text");
        Assert.False(inflight.HasOutput);
        Assert.True(parser.HasBuffered);

        var residue = parser.Flush();
        Assert.Equal("<think>never closed reasoning text", residue);
        Assert.False(parser.HasBuffered);
    }

    [Fact]
    public void Flush_returns_null_when_buffer_is_empty()
    {
        var parser = new ThinkTagParser();
        Assert.Null(parser.Flush());
    }

    [Fact]
    public void Plain_state_passes_subsequent_chunks_through_unchanged()
    {
        var parser = new ThinkTagParser();

        // First message: complete think + plain body switches to Plain state.
        // Parser only TrimStart()'s after-think text, so trailing whitespace is preserved.
        var first = parser.Ingest("<think>warmup</think>hello ");
        Assert.Equal("hello ", first.plainText);

        // Subsequent chunks should pass through verbatim (incl. literal angle brackets).
        var second = parser.Ingest("world ");
        Assert.Equal("world ", second.plainText);
        Assert.Null(second.reasoning);

        var third = parser.Ingest("<not-a-think>");
        Assert.Equal("<not-a-think>", third.plainText);
        Assert.Null(third.reasoning);
    }

    [Fact]
    public void Reset_returns_parser_to_detecting_state_for_next_agent()
    {
        var parser = new ThinkTagParser();

        parser.Ingest("<think>first agent</think>output");
        Assert.True(parser.ThinkComplete);

        parser.Reset();
        Assert.False(parser.ThinkComplete);
        Assert.False(parser.HasBuffered);

        // After reset, a new <think> block is parsed fresh.
        var result = parser.Ingest("<think>second agent</think>second body");
        Assert.Equal("second agent", result.reasoning);
        Assert.Equal("second body", result.plainText);
    }

    [Fact]
    public void Permissive_open_tag_with_attribute_is_handled()
    {
        var parser = new ThinkTagParser();

        // Defensive belt: some models emit <think foo>...</think>.
        var result = parser.Ingest("<think attr=\"x\">attributed reasoning</think>body");

        Assert.Equal("attributed reasoning", result.reasoning);
        Assert.Equal("body", result.plainText);
    }

    [Fact]
    public void Reasoning_and_body_whitespace_is_trimmed()
    {
        var parser = new ThinkTagParser();

        var result = parser.Ingest("<think>   padded reasoning   </think>   padded body");

        Assert.Equal("padded reasoning", result.reasoning);
        Assert.Equal("padded body", result.plainText);
    }

    [Fact]
    public void Empty_ingest_with_buffered_content_below_threshold_holds()
    {
        var parser = new ThinkTagParser(detectionThreshold: 100);

        var result = parser.Ingest("short");
        Assert.False(result.HasOutput);
        Assert.True(parser.HasBuffered);
        Assert.False(parser.ThinkComplete);

        // Flush at end-of-stream surfaces the held content for the caller to emit.
        var residue = parser.Flush();
        Assert.Equal("short", residue);
    }
}
