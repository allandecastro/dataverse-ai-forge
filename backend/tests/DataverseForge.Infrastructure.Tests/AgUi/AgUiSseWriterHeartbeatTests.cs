using System.Text;
using DataverseForge.Infrastructure.AgUi;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;

namespace DataverseForge.Infrastructure.Tests.AgUi;

/// <summary>
/// A silent stretch in a run (a long tool call) must still produce bytes on the wire,
/// otherwise idle timeouts in between (the Next.js proxy's 5-minute body timeout) cut
/// the stream. The heartbeat is an SSE comment, so it never becomes an event.
/// </summary>
public class AgUiSseWriterHeartbeatTests
{
    private static async IAsyncEnumerable<ChatResponseUpdate> SlowStream(TimeSpan silence)
    {
        var first = new ChatResponseUpdate();
        first.Contents.Add(new TextContent("working"));
        yield return first;

        await Task.Delay(silence); // e.g. a long tool call

        var last = new ChatResponseUpdate();
        last.Contents.Add(new TextContent(" done"));
        yield return last;
    }

    [Fact]
    public async Task Silent_stretch_emits_ping_comments_and_stops_after_the_run()
    {
        var original = AgUiSseWriter.HeartbeatInterval;
        AgUiSseWriter.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
        try
        {
            var body = new MemoryStream();
            var context = new DefaultHttpContext();
            context.Response.Body = body;

            await AgUiSseWriter.WriteStreamAsync(
                context.Response, "t", "r", "SolutionArchitect",
                SlowStream(TimeSpan.FromMilliseconds(400)), CancellationToken.None);

            var text = Encoding.UTF8.GetString(body.ToArray());
            Assert.Contains(": ping\n\n", text);

            // Frames are never interleaved: every frame is either a ping or a whole data event.
            foreach (var frame in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
                Assert.True(frame == ": ping" || frame.StartsWith("data: {", StringComparison.Ordinal), frame);

            // RUN_FINISHED is the last event, and nothing is written after the run returns.
            var lengthAtReturn = body.Length;
            Assert.EndsWith("\"type\":\"RUN_FINISHED\",\"threadId\":\"t\",\"runId\":\"r\"}\n\n", text);
            await Task.Delay(200);
            Assert.Equal(lengthAtReturn, body.Length);
        }
        finally
        {
            AgUiSseWriter.HeartbeatInterval = original;
        }
    }
}
