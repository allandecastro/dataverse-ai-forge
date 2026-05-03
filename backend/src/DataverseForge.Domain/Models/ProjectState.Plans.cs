namespace DataverseForge.Domain.Models;

public partial class ProjectState
{
    // ─── PENDING WORK ───
    public List<ProposedChange> PendingChanges { get; set; } = [];

    /// <summary>
    /// Planned OData payloads keyed by planId — populated by plan_* tools, consumed by
    /// execute_* tools. Prevents the agent from mangling the OData JSON between plan
    /// and execute by keeping the payload server-side.
    /// </summary>
    public Dictionary<string, string> PendingPlans { get; set; } = [];

    /// <summary>
    /// When each <see cref="PendingPlans"/> entry was created (UTC). Used to age
    /// out abandoned approvals — a user who closed their tab mid-approval shouldn't
    /// leave a plan dangling forever. Stale entries get pruned on the next save.
    /// New keys appearing in PendingPlans without a matching entry here default to
    /// "unknown age" and are kept (back-compat for sessions saved before this field).
    /// </summary>
    public Dictionary<string, DateTime> PendingPlanTimestamps { get; set; } = [];

    /// <summary>
    /// Plans that have already executed successfully. Same key space as PendingPlans.
    /// Lets the Tester verify against the actual planned values instead of inventing
    /// expected values from conversation history.
    /// </summary>
    public Dictionary<string, string> ExecutedPlans { get; set; } = [];

    /// <summary>
    /// Hard cap on how long a pending plan can sit before it's eligible for pruning.
    /// 24h matches user expectations: "I closed the tab yesterday" → "your plan was
    /// cleaned up; please ask again". Tune via configuration if a longer human-in-the-
    /// loop workflow needs it.
    /// </summary>
    public static readonly TimeSpan PendingPlanTtl = TimeSpan.FromHours(24);

    /// <summary>
    /// Atomic helper — keeps <see cref="PendingPlans"/> and
    /// <see cref="PendingPlanTimestamps"/> in lockstep so a future Plan_* tool
    /// can't accidentally write the payload without the timestamp (which would
    /// make the entry immune to TTL pruning).
    /// </summary>
    public void AddPendingPlan(string planId, string payloadJson)
    {
        PendingPlans[planId] = payloadJson;
        PendingPlanTimestamps[planId] = DateTime.UtcNow;
    }

    /// <summary>
    /// Looks up a planId across both <see cref="PendingPlans"/> and
    /// <see cref="ExecutedPlans"/> so Execute tools can give a precise
    /// recovery message in three cases:
    ///   • Pending → return the payload, ready to execute.
    ///   • Already executed → return null + status = AlreadyExecuted so
    ///     the tool tells the agent "this work is done; don't retry; move on".
    ///   • Truly missing → return null + status = NotFound so the tool tells
    ///     the agent to call plan_* first.
    ///
    /// Past failure: agents misread "Plan not found" as "I should plan again"
    /// when in fact the plan had already executed earlier this session. They
    /// looped re-planning components that already existed.
    /// </summary>
    public PlanLookupStatus LookupPlan(string planId, out string payloadJson)
    {
        if (PendingPlans.TryGetValue(planId, out var payload))
        {
            payloadJson = payload;
            return PlanLookupStatus.Pending;
        }
        // Empty (not null) when the plan isn't pending — keeps the call sites
        // free of `string?` null checks. They branch on the returned status
        // and only touch the out parameter on the Pending path.
        payloadJson = string.Empty;
        if (ExecutedPlans.ContainsKey(planId))
        {
            return PlanLookupStatus.AlreadyExecuted;
        }
        return PlanLookupStatus.NotFound;
    }

    /// <summary>
    /// Drop pending plans older than <see cref="PendingPlanTtl"/>. Returns the
    /// list of pruned planIds (caller can audit-log them). Plans without a
    /// timestamp (saved before the TTL feature shipped) are kept — we don't
    /// know their age, so we err on the side of preservation.
    /// </summary>
    public IReadOnlyList<string> PruneStalePendingPlans(DateTime now)
    {
        if (PendingPlans.Count == 0) return Array.Empty<string>();

        var threshold = now - PendingPlanTtl;
        var toRemove = new List<string>();
        foreach (var (planId, _) in PendingPlans)
        {
            if (PendingPlanTimestamps.TryGetValue(planId, out var createdAt) && createdAt < threshold)
            {
                toRemove.Add(planId);
            }
        }
        foreach (var planId in toRemove)
        {
            PendingPlans.Remove(planId);
            PendingPlanTimestamps.Remove(planId);
        }
        return toRemove;
    }
}
