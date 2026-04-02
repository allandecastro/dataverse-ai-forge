using System.ComponentModel;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Server-side tool that records an approval request. The frontend detects this
/// tool call via the SSE TOOL_CALL events and renders an ApprovalCard. The user's
/// decision is sent back as the next chat message.
/// </summary>
public class RequestApprovalTool
{
    [Description("Request user approval before executing a Dataverse write operation. "
        + "Returns a confirmation that the request was presented to the user. "
        + "WAIT for the user's next message before proceeding.")]
    public Task<string> RequestApprovalAsync(
        [Description("The operation being requested (e.g., CreateTable, CreateField, CreateRelationship)")] string operationType,
        [Description("Human-readable description of what will happen")] string description,
        [Description("Risk classification: green (low), yellow (medium), orange (high), red (critical)")] string riskLevel,
        [Description("JSON payload of the planned operation details")] string details,
        CancellationToken cancellationToken)
    {
        // This tool is intentionally a no-op on the server.
        // Its purpose is to surface the approval request to the user via
        // the SSE TOOL_CALL events. The frontend renders an ApprovalCard
        // and the user's decision comes back as the next message.
        var result = $"Approval requested for {operationType}: {description}. "
            + $"Risk level: {riskLevel}. "
            + "Waiting for user decision. Do NOT proceed until the user responds.";
        return Task.FromResult(result);
    }
}
