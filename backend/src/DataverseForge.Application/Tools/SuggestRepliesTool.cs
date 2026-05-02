using System.ComponentModel;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Lets an agent surface 2–4 likely user replies as clickable chips below its
/// last message. Each suggestion is a short string written in the user's voice
/// (e.g., "Yes, proceed", "Use existing solution", "Skip the optional fields").
///
/// The tool itself is a no-op vehicle — its arguments are intercepted by
/// <c>FunctionCallEmitter</c>, which translates them into a STATE_DELTA on
/// <c>/suggestedReplies</c> for the frontend to render. The tool returns a
/// short ack so the agent can move on without retry loops.
/// </summary>
public class SuggestRepliesTool
{
    [Description("Surface 2–4 short suggested replies as clickable chips under your last message. "
        + "Use ONLY when you've just asked the user a question and there are clear, likely answers. "
        + "Each suggestion must be in the user's voice (1st person), ≤80 chars. "
        + "Examples: ['Yes, create the table', 'No, just review', 'Show me the plan first']. "
        + "Don't call when the user must provide free-form data (names, descriptions, lists).")]
    public Task<string> SuggestRepliesAsync(
        [Description("Array of 2–4 short suggested user replies, each ≤80 characters")] string[] suggestions,
        CancellationToken cancellationToken)
    {
        var count = suggestions?.Length ?? 0;
        if (count == 0) return Task.FromResult("ok (no suggestions)");
        return Task.FromResult($"ok ({count} suggestions surfaced)");
    }
}
