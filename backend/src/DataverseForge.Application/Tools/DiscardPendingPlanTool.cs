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
    [Description("Discard one OR many pending plans from project state WITHOUT touching Dataverse. "
        + "Use this whenever plans are no longer needed (already-exists, user changed scope, denied "
        + "approval, plan failed and you want to abandon the rest of a batch, or handoff_to_tester is "
        + "blocked because of stale PendingPlans). Does NOT delete anything in Dataverse — it only "
        + "removes the server-side planId entries. Safe to call without approval. "
        + "Pass a single planId, a comma-separated list of planIds, or the literal string 'all' to "
        + "drop every pending plan in one call.")]
    public async Task<string> DiscardPendingPlanAsync(
        [Description("Single planId, comma-separated list of planIds (e.g. 'create_field_abc, modify_field_xyz'), "
            + "or the literal 'all' to discard every pending plan. Match planIds verbatim from previous plan_* calls.")] string planId,
        [Description("Short reason for discarding (e.g. 'option set already exists', 'user changed scope', "
            + "'cleanup before tester handoff'). Goes to the audit log.")] string reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(planId))
            return "ERROR: planId is required (single planId, comma-list, or 'all').";

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (state.PendingPlans.Count == 0)
        {
            return "NOOP: PendingPlans is empty — nothing to discard.";
        }

        var beforeCount = state.PendingPlans.Count;
        List<string> targetIds;
        if (string.Equals(planId.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            targetIds = state.PendingPlans.Keys.ToList();
        }
        else
        {
            targetIds = planId
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        var dropped = new List<string>();
        var notFound = new List<string>();
        foreach (var pid in targetIds)
        {
            if (state.PendingPlans.Remove(pid))
            {
                state.PendingPlanTimestamps.Remove(pid);
                dropped.Add(pid);
            }
            else
            {
                notFound.Add(pid);
            }
        }

        if (dropped.Count == 0)
        {
            logger.LogInformation("DiscardPendingPlan: no matches in PendingPlans for input '{Input}'", planId);
            return $"NOOP: None of [{string.Join(", ", targetIds)}] are in PendingPlans "
                + "(already executed, already discarded, or never existed).";
        }

        state.AuditLog.Add(new AuditEntry
        {
            EntryId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.UtcNow,
            AgentName = AgentNames.DataModelBuilder,
            Action = "DiscardPendingPlan",
            Detail = $"Discarded {dropped.Count} pending plan(s): [{string.Join(", ", dropped)}]. "
                + $"Reason: {reason}. Pending count {beforeCount} → {state.PendingPlans.Count}.",
            Success = true
        });

        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        logger.LogInformation("Discarded {Count} pending plan(s) (reason={Reason}); remaining={Remaining}",
            dropped.Count, reason, state.PendingPlans.Count);

        var msg = $"OK: discarded {dropped.Count} plan(s) [{string.Join(", ", dropped)}]. Reason: {reason}.";
        if (notFound.Count > 0)
            msg += $" Not in PendingPlans: [{string.Join(", ", notFound)}].";
        msg += $" Remaining pending: {state.PendingPlans.Count}.";
        if (state.PendingPlans.Count == 0)
            msg += " You can now hand off to Tester or proceed with new work.";
        return msg;
    }
}
