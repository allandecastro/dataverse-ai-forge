using System.ComponentModel;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Server-side tool that records a BATCH approval request — one user decision
/// covers many planned operations (e.g., creating 12 fields on a new table).
/// The frontend detects this tool call via SSE TOOL_CALL events and renders a
/// batch ApprovalCard with a single Approve / Reject for the whole list.
/// </summary>
public class RequestBatchApprovalTool
{
    [Description("Request user approval for a BATCH of related operations in one card. "
        + "Use this instead of calling RequestApproval N times when you're about to run "
        + "many similar plans (e.g., creating multiple fields on the same table, or "
        + "adding multiple options to a Choice). "
        + "items is a JSON array of objects: "
        + "[{\"planId\":\"abc\",\"summary\":\"Create field FirstName (string,100)\",\"riskLevel\":\"green\"}, ...]. "
        + "Returns immediately — WAIT for the user's next message before executing.")]
    public Task<string> RequestBatchApprovalAsync(
        [Description("Top-level operation type the batch represents (e.g., CreateFields, AddOptions)")] string operationType,
        [Description("One-line summary of what the batch will do")] string description,
        [Description("Worst-case risk level across the batch: green | yellow | orange | red")] string riskLevel,
        [Description("JSON array of {planId, summary, riskLevel} objects — one per planned operation")] string items,
        CancellationToken cancellationToken)
    {
        // No-op on the server. Surfaces the request to the frontend via SSE TOOL_CALL.
        var result = $"Batch approval requested for {operationType}: {description}. "
            + $"Risk level: {riskLevel}. "
            + "Waiting for user decision. Do NOT execute any plan in the batch until the user responds.";
        return Task.FromResult(result);
    }
}
