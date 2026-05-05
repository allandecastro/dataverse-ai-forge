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
    /// How long a same-kind pending plan must sit before
    /// <see cref="AddPendingPlan"/> considers it stale and sweeps it. Tighter
    /// than <see cref="PendingPlanTtl"/> because the sweep is meant to clean
    /// up "narrate-without-firing" orphans — those typically come from a
    /// previous user turn (so 5 min is plenty), while a legitimate batch of
    /// same-kind plans is added within seconds and must NOT be touched.
    ///
    /// Past failure (real session): the sweep was unconditional. Agent
    /// planned 3 create_field_* plans in sequence to build a batch — each
    /// new plan dropped the previous one, so only the LAST plan survived
    /// under PendingPlans. Execute then failed with "plan not found" for
    /// the first two, costing the user 6 minutes of replan-execute-replan
    /// churn.
    /// </summary>
    public static readonly TimeSpan SameKindOrphanThreshold = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Atomic helper — keeps <see cref="PendingPlans"/> and
    /// <see cref="PendingPlanTimestamps"/> in lockstep so a future Plan_* tool
    /// can't accidentally write the payload without the timestamp (which would
    /// make the entry immune to TTL pruning).
    ///
    /// Sweeps orphan pending plans of the same KIND that are OLDER than
    /// <see cref="SameKindOrphanThreshold"/>. Recent same-kind plans are
    /// preserved — they are almost always parts of a legitimate batch (e.g.
    /// the agent planning 4 create_field_* in a row before executing them).
    /// Only stale orphans from previous turns are dropped.
    /// </summary>
    public void AddPendingPlan(string planId, string payloadJson)
    {
        SweepOrphansOfSameKind(planId, DateTime.UtcNow);
        PendingPlans[planId] = payloadJson;
        PendingPlanTimestamps[planId] = DateTime.UtcNow;
    }

    /// <summary>
    /// Returns the kind prefix of a planId (e.g. <c>update_form_</c>). Returns
    /// empty when the planId doesn't match the expected shape — caller treats
    /// that as "no sweep" rather than guessing.
    ///
    /// Public so introspection tools (e.g. ListPendingPlans) can label plan
    /// kinds in their output.
    /// </summary>
    public static string ExtractPlanKind(string planId)
    {
        if (string.IsNullOrEmpty(planId)) return string.Empty;
        // Trailing "_{32 hex chars}" identifies a real plan; strip it.
        const int guidLen = 32;
        if (planId.Length <= guidLen + 1) return string.Empty;
        var separatorIdx = planId.Length - guidLen - 1;
        if (planId[separatorIdx] != '_') return string.Empty;
        for (var i = separatorIdx + 1; i < planId.Length; i++)
        {
            var c = planId[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return string.Empty;
        }
        return planId[..(separatorIdx + 1)];
    }

    /// <summary>
    /// Internal: drop pending plans whose kind prefix matches the new plan's
    /// kind AND whose timestamp is older than
    /// <see cref="SameKindOrphanThreshold"/>. Recent same-kind plans are kept
    /// (they're part of a legitimate batch); only stale ones from prior turns
    /// get swept.
    /// Plans without a timestamp (saved before the timestamp tracking shipped)
    /// are conservatively kept — we don't know their age, so we err on the
    /// side of preservation.
    /// </summary>
    private void SweepOrphansOfSameKind(string newPlanId, DateTime now)
    {
        var kind = ExtractPlanKind(newPlanId);
        if (string.IsNullOrEmpty(kind)) return;

        var staleCutoff = now - SameKindOrphanThreshold;

        // Collect first, then mutate — Dictionary throws on enumerate-during-modify.
        var toRemove = new List<string>();
        foreach (var (existingId, _) in PendingPlans)
        {
            if (existingId == newPlanId) continue;
            if (!existingId.StartsWith(kind, StringComparison.Ordinal)) continue;
            // Without a timestamp, default to keeping the plan (back-compat).
            if (!PendingPlanTimestamps.TryGetValue(existingId, out var createdAt)) continue;
            if (createdAt < staleCutoff)
            {
                toRemove.Add(existingId);
            }
        }
        foreach (var id in toRemove)
        {
            PendingPlans.Remove(id);
            PendingPlanTimestamps.Remove(id);
        }
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
