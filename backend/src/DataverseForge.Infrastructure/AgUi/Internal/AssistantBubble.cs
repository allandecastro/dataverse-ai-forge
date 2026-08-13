using System.Text;
using Microsoft.AspNetCore.Http;

namespace DataverseForge.Infrastructure.AgUi.Internal;

/// <summary>
/// Tracks the "currently open assistant bubble" state and emits the matching
/// TEXT_MESSAGE_START / TEXT_MESSAGE_CONTENT / TEXT_MESSAGE_END events. Lazy:
/// no START is emitted until there's actual content to write.
///
/// Past regression: emitting TEXT_MESSAGE_START with no follow-up content
/// produced empty bubbles in the chat. This class only opens when content arrives.
/// </summary>
internal sealed class AssistantBubble
{
    private readonly Func<object, CancellationToken, Task> _writeEvent;
    private readonly string _threadId;
    private readonly string _runId;
    private string _agentName;
    private string _messageId = Guid.NewGuid().ToString("N");
    private bool _started;
    // Buffer of all text written into this bubble. Used for end-of-bubble
    // post-processing (e.g. detecting `[suggest_replies]` markup when the
    // agent inlined the suggestions instead of calling the SuggestReplies tool).
    private readonly StringBuilder _buffer = new();

    public AssistantBubble(
        HttpResponse response,
        string threadId,
        string runId,
        string agentName,
        Func<HttpResponse, object, CancellationToken, Task> writeEvent)
    {
        _writeEvent = (payload, ct) => writeEvent(response, payload, ct);
        _threadId = threadId;
        _runId = runId;
        _agentName = agentName;
    }

    public string MessageId => _messageId;
    public string AgentName => _agentName;
    public bool IsOpen => _started;
    /// <summary>The full text written into this bubble since the last <see cref="StartNewBubbleFor"/>.</summary>
    public string BufferedText => _buffer.ToString();

    public async Task WriteContentAsync(string text, CancellationToken ct)
    {
        if (!_started)
        {
            await _writeEvent(new
            {
                type = "TEXT_MESSAGE_START",
                threadId = _threadId,
                runId = _runId,
                messageId = _messageId,
                role = "assistant",
                agentName = _agentName,
            }, ct);
            _started = true;
        }
        _buffer.Append(text);
        await _writeEvent(new
        {
            type = "TEXT_MESSAGE_CONTENT",
            threadId = _threadId,
            runId = _runId,
            messageId = _messageId,
            delta = text,
        }, ct);
    }

    public async Task CloseAsync(CancellationToken ct)
    {
        if (!_started) return;
        await _writeEvent(new
        {
            type = "TEXT_MESSAGE_END",
            threadId = _threadId,
            runId = _runId,
            messageId = _messageId,
        }, ct);
        _started = false;
    }

    /// <summary>Switch to a new agent + new messageId. Caller is responsible for
    /// closing the prior bubble first if it was open.</summary>
    public void StartNewBubbleFor(string agentName)
    {
        _agentName = agentName;
        _messageId = Guid.NewGuid().ToString("N");
        _started = false;
        _buffer.Clear();
    }

    /// <summary>Continue the SAME agent's turn in a fresh bubble: mint a new
    /// messageId so text that RESUMES after a tool call opens a new bubble, which
    /// keeps tool rows rendered between the pre- and post-tool text. Keeps the
    /// agent and the running buffer (the buffer feeds end-of-turn suggest-replies
    /// / yes-no chip detection over the whole turn's text). Caller must close the
    /// prior bubble first.</summary>
    public void StartNewSegment()
    {
        _messageId = Guid.NewGuid().ToString("N");
        _started = false;
    }
}
