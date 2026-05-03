using DataverseForge.Application.Agents;
using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;

namespace DataverseForge.Application.Tools;

public class HandoffToTesterTool(IProjectStateStore projectStateStore, ISessionContext sessionContext)
{
    [Description("Hand off the conversation to the Tester agent for post-execution verification. "
        + "Use this AFTER you have run execute_* tools and they returned success. "
        + "The Tester verifies created components against their planned specification. "
        + "INCLUDE the planId(s) returned by execute_* tools in your summary — the Tester uses them "
        + "to call verify_*_against_plan (no false-FAILs from invented expected values). "
        + "DO NOT call this tool before execution — if you call it while plans are still pending "
        + "approval/execution, the tool will refuse the handoff.")]
    public async Task<string> HandoffToTesterAsync(
        [Description("Summary of what was created or modified — MUST include the planId(s) from the "
            + "most recent execute_* tool calls so the Tester can use verify_*_against_plan tools. "
            + "Pass the text under THIS parameter name (`summary`); aliases `designSpec` and "
            + "`schemaSpec` are accepted as a fallback in case the tool-call binder picks one of those.")] string summary,
        CancellationToken cancellationToken,
        [Description("ALIAS for `summary`. Optional. Used only when `summary` is empty.")] string designSpec = "",
        [Description("ALIAS for `summary`. Optional. Used only when `summary` and `designSpec` are empty.")] string schemaSpec = "")
    {
        // Tolerate the agent picking the wrong parameter name. The other handoff
        // tools take `schemaSpec` (DMB) or `designSpec` (Reviewer); past failure:
        // the model overgeneralized and called HandoffToTester with `designSpec`,
        // which the Microsoft.Extensions.AI binder rejected with a generic
        // "Function failed." Accept any of the three names and merge — the
        // semantics are identical from the Tester's perspective.
        var effective = !string.IsNullOrWhiteSpace(summary) ? summary
            : !string.IsNullOrWhiteSpace(designSpec) ? designSpec
            : schemaSpec;

        // Guard: agents have repeatedly called handoff_to_tester between plan_* and execute_*
        // (i.e. before user approval, before anything was actually built). The Tester then
        // looks up the planId in ExecutedPlans, finds nothing, and reports a confusing FAIL.
        // Refuse the handoff if any plans are still pending — force the builder to execute first.
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        if (state.PendingPlans.Count > 0)
        {
            var pendingIds = string.Join(", ", state.PendingPlans.Keys);
            return "ERROR: Cannot hand off to Tester yet — there are plans waiting to be executed: "
                + $"[{pendingIds}]. Choose ONE recovery path: "
                + "(a) for each planId you still want, call request_approval then execute_*; "
                + "(b) call discard_pending_plan (planId: '<id>' or 'pid1, pid2' or 'all'; "
                + "reason: 'why') to drop stale ones, then retry handoff_to_tester. "
                + "Tester only verifies things that have actually been executed in Dataverse.";
        }

        return await HandoffTool.ExecuteHandoffAsync(projectStateStore, sessionContext, AgentNames.Tester, "Tester", effective, cancellationToken);
    }
}
