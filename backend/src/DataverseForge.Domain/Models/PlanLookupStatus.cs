namespace DataverseForge.Domain.Models;

/// <summary>
/// Outcome of <see cref="ProjectState.LookupPlan"/>. Three-way distinction so
/// Execute tools can return precise messages — most importantly, distinguish
/// "this plan already ran" from "this plan never existed", because agents
/// have routinely confused the two and looped re-planning completed work.
/// </summary>
public enum PlanLookupStatus
{
    /// <summary>Plan is in <see cref="ProjectState.PendingPlans"/> and ready to execute.</summary>
    Pending,
    /// <summary>Plan ran successfully earlier this session; the artifact already exists.</summary>
    AlreadyExecuted,
    /// <summary>Plan was never stored — caller must invoke the matching plan_* tool first.</summary>
    NotFound,
}
