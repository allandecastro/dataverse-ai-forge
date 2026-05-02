using System.ComponentModel;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Non-destructive plan cancellation. Removes a planId from project state's
/// PendingPlans dictionary WITHOUT touching Dataverse.
///
/// Past regression: when plan_create_global_option_set's payload turned out to
/// already exist in Dataverse, the plan stayed in PendingPlans. The agent then
/// invented destructive "cancellation" steps — calling DeleteOptionValue on the
/// existing live option set — to "discard" it. That's wrong on two levels:
///   1. The pending plan is just a server-side JSON entry, not a Dataverse object.
///   2. The existing option set in Dataverse belongs to the user; deleting its
///      options would corrupt their environment.
///
/// This tool exists so the agent has a clear, safe answer for "discard the plan":
/// just drop the entry. No Dataverse write, no risk.
/// </summary>
public class DiscardPendingPlanTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<DiscardPendingPlanTool> logger)
{
    [Description("Discard a pending plan from project state WITHOUT touching Dataverse. "
        + "Use this whenever a plan is no longer needed (already-exists, user changed "
        + "their mind, planned but no longer required). Does NOT delete anything in "
        + "Dataverse — it only removes the server-side planId entry. Safe to call "
        + "without approval.")]
    public async Task<string> DiscardPendingPlanAsync(
        [Description("planId to remove from PendingPlans (verbatim from a previous plan_* call)")] string planId,
        [Description("Short reason for discarding (e.g. 'option set already exists', 'user changed scope'). "
            + "Goes to the audit log.")] string reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planId))
            return "ERROR: planId is required.";

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (!state.PendingPlans.Remove(planId))
        {
            logger.LogInformation("DiscardPendingPlan: planId {PlanId} not in PendingPlans — nothing to do", planId);
            return $"NOOP: Plan '{planId}' is not in PendingPlans (already executed, already discarded, or never existed).";
        }

        state.AuditLog.Add(new AuditEntry
        {
            EntryId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.UtcNow,
            AgentName = AgentNames.DataModelBuilder,
            Action = "DiscardPendingPlan",
            Detail = $"Discarded planId '{planId}'. Reason: {reason}",
            Success = true
        });

        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        logger.LogInformation("Discarded pending plan {PlanId}: {Reason}", planId, reason);
        return $"OK: Pending plan '{planId}' discarded. Reason: {reason}. "
            + "No Dataverse changes were made. You can now proceed with the next step "
            + "(e.g. handoff_to_tester) without this plan blocking you.";
    }
}
