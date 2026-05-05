using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Read-only introspection of <c>state.PendingPlans</c>. Agents that have lost
/// track of a planId across turns (long conversations, batches, post-approval
/// confusion) call this instead of guessing or claiming "plans expired".
///
/// Past regression: after a 78-second wait between plan_* and execute_*, the
/// agent reported "plans expired or were lost" and re-planned from scratch —
/// even though the plans were still pending. The agent had no way to verify
/// state without writing to Dataverse. This tool closes that gap.
/// </summary>
public class ListPendingPlansTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("List all currently pending plans (created via plan_* but not yet executed). "
        + "Read-only, idempotent. Use this BEFORE claiming a plan was lost — the agent often "
        + "still has live plans in state but loses the planIds in long conversations. Call this "
        + "when an approval message arrives and you're not 100% sure which planIds to execute, "
        + "or when you're about to discard plans and want to confirm what's actually pending. "
        + "Returns each plan's planId, kind prefix (e.g. 'create_field_'), and age in seconds.")]
    public async Task<IReadOnlyList<PendingPlanInfo>> ListPendingPlansAsync(CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        var now = DateTime.UtcNow;
        return state.PendingPlans.Keys
            .Select(planId =>
            {
                var kind = ProjectState.ExtractPlanKind(planId);
                var ageSeconds = state.PendingPlanTimestamps.TryGetValue(planId, out var ts)
                    ? (int)(now - ts).TotalSeconds
                    : -1;
                return new PendingPlanInfo(
                    PlanId: planId,
                    Kind: string.IsNullOrEmpty(kind) ? "unknown" : kind.TrimEnd('_'),
                    AgeSeconds: ageSeconds);
            })
            .OrderBy(p => p.AgeSeconds)
            .ToList();
    }
}

/// <summary>
/// Lightweight projection of one pending plan. Mirrors the agent-facing fields
/// of <c>state.PendingPlans</c> + <c>state.PendingPlanTimestamps</c> without
/// leaking the OData payload (which would bloat the agent's context).
/// </summary>
public sealed record PendingPlanInfo(
    string PlanId,
    string Kind,
    int AgeSeconds);
