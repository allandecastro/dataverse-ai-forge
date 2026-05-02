using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Server-side tool that records an approval request. The frontend detects this
/// tool call via the SSE TOOL_CALL events and renders an ApprovalCard. The user's
/// decision is sent back as the next chat message.
/// </summary>
public class RequestApprovalTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Request user approval before executing a Dataverse write operation. "
        + "Returns a confirmation that the request was presented to the user. "
        + "WAIT for the user's next message before proceeding. "
        + "The `details` field MUST be a JSON object that includes the planId from the "
        + "most recent plan_* call — the tool validates the planId against pending plans "
        + "and refuses fabricated ones.")]
    public async Task<string> RequestApprovalAsync(
        [Description("The operation being requested (e.g., CreateTable, CreateField, CreateRelationship)")] string operationType,
        [Description("Human-readable description of what will happen")] string description,
        [Description("Risk classification: green (low), yellow (medium), orange (high), red (critical)")] string riskLevel,
        [Description("JSON payload — MUST include {\"planId\":\"...\"} matching a pending plan from a recent plan_* tool result")] string details,
        CancellationToken cancellationToken)
    {
        // Code guard: extract planId from details and verify it exists in PendingPlans.
        // Without this, agents have asked for approval against fabricated planIds, and
        // the user's "Approve" reply led to a confused execute_* failure on the next turn.
        var extractedPlanId = TryExtractPlanId(details);
        if (extractedPlanId is not null)
        {
            var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
            if (!state.PendingPlans.ContainsKey(extractedPlanId))
            {
                var pending = state.PendingPlans.Count == 0
                    ? "(no plans are pending)"
                    : "Pending: [" + string.Join(", ", state.PendingPlans.Keys) + "]";
                return $"ERROR: planId '{extractedPlanId}' is not in PendingPlans. Did you call plan_* first? "
                    + pending + ". Call plan_* and use the returned planId verbatim.";
            }
        }

        var result = $"Approval requested for {operationType}: {description}. "
            + $"Risk level: {riskLevel}. "
            + "Waiting for user decision. Do NOT proceed until the user responds.";
        return result;
    }

    private static string? TryExtractPlanId(string details)
    {
        if (string.IsNullOrWhiteSpace(details)) return null;
        try
        {
            using var doc = JsonDocument.Parse(details);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("planId", out var pid) &&
                pid.ValueKind == JsonValueKind.String)
            {
                return pid.GetString();
            }
        }
        catch
        {
            // Not valid JSON — agents sometimes pass plain prose. Skip the guard rather than fail.
        }
        return null;
    }
}
