using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DataverseForge.Infrastructure.Tests.Integration;

/// <summary>
/// End-to-end smoke test for the AG-UI SSE pipeline — the chain CLAUDE.md
/// bug-pattern #5 warns is untested as a whole: HTTP request → endpoint →
/// OrchestratingChatClient → AgUiSseWriter framing → SSE wire format.
///
/// The test host has no Azure AI credentials, so OrchestratingChatClient takes
/// its graceful "not configured" fallback and streams a friendly text message.
/// That still exercises the FULL pipeline end-to-end (RUN_STARTED → STATE_DELTA →
/// TEXT_MESSAGE_* → RUN_FINISHED), which no other test covers — every unit test
/// mocks at most one link of this chain.
/// </summary>
[Trait("Category", "Integration")]
public class AgUiStreamSmokeTests : IClassFixture<InMemoryWebAppFactory>
{
    private readonly InMemoryWebAppFactory _factory;

    public AgUiStreamSmokeTests(InMemoryWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostAgUi_StreamsWellFormedSseLifecycle()
    {
        using var client = _factory.CreateClient();

        var res = await client.PostAsJsonAsync("/api/agui", new
        {
            threadId = "smoke-thread",
            runId = "smoke-run",
            messages = new[] { new { role = "user", content = "Hello" } },
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/event-stream", res.Content.Headers.ContentType?.MediaType);

        var body = await res.Content.ReadAsStringAsync();

        // Full SSE lifecycle made it onto the wire — proves the pipeline ran from
        // HTTP all the way through AgUiSseWriter framing.
        Assert.Contains("data:", body);
        Assert.Contains("RUN_STARTED", body);
        Assert.Contains("RUN_FINISHED", body);

        // The graceful "AI not configured" message streamed as real text content
        // (exercises ThinkTagParser → AssistantBubble → TEXT_MESSAGE_* framing).
        Assert.Contains("not configured", body);

        // Regression guard for the SSE error-message leak fix: no raw exception
        // text or stack frames should ever reach the client over SSE.
        Assert.DoesNotContain("at DataverseForge", body);
        Assert.DoesNotContain("StackTrace", body);
        Assert.DoesNotContain("System.Exception", body);
    }

    [Fact]
    public async Task PostAgUi_ToleratesEmptyMessagesArray()
    {
        // The endpoint must not 500 on a degenerate-but-valid body — it should
        // still open and close a clean stream.
        using var client = _factory.CreateClient();

        var res = await client.PostAsJsonAsync("/api/agui", new
        {
            threadId = "smoke-empty",
            runId = "smoke-empty-run",
            messages = Array.Empty<object>(),
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("RUN_STARTED", body);
        Assert.Contains("RUN_FINISHED", body);
    }
}
