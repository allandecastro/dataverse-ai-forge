using DataverseForge.Domain.Models;

namespace DataverseForge.Domain.Tests;

public class ProjectStateTests
{
    [Fact]
    public void Defaults_AreSafeForFreshState()
    {
        var s = new ProjectState();

        Assert.Equal(string.Empty, s.ProjectId);
        Assert.Equal(string.Empty, s.ProjectName);
        Assert.Equal("SolutionArchitect", s.ActiveAgentName);
        Assert.False(s.EnvironmentConfirmed);
        Assert.False(s.SolutionConfirmed);
        Assert.False(s.IsReady);
    }

    [Fact]
    public void EmptyCollections_AreInitialized()
    {
        // Symptom this guards: a partial-class consumer null-refs because a list
        // wasn't initialized. All collections must be ready to use on a fresh state.
        var s = new ProjectState();

        Assert.NotNull(s.Tables);
        Assert.NotNull(s.Fields);
        Assert.NotNull(s.Relationships);
        Assert.NotNull(s.Forms);
        Assert.NotNull(s.Views);
        Assert.NotNull(s.PendingPlans);
        Assert.NotNull(s.ExecutedPlans);
        Assert.NotNull(s.OperationStack);
        Assert.NotNull(s.AuditLog);

        Assert.Empty(s.Tables);
        Assert.Empty(s.Fields);
        Assert.Empty(s.PendingPlans);
        Assert.Empty(s.ExecutedPlans);
        Assert.Empty(s.OperationStack);
        Assert.Empty(s.AuditLog);
    }

    // ─── IsReady: only true when both env AND solution confirmed ───

    [Fact]
    public void IsReady_FalseWhenNeitherConfirmed()
    {
        var s = new ProjectState();
        Assert.False(s.IsReady);
    }

    [Fact]
    public void IsReady_FalseWhenOnlyEnvironmentConfirmed()
    {
        var s = new ProjectState { EnvironmentConfirmed = true };
        Assert.False(s.IsReady);
    }

    [Fact]
    public void IsReady_FalseWhenOnlySolutionConfirmed()
    {
        var s = new ProjectState { SolutionConfirmed = true };
        Assert.False(s.IsReady);
    }

    [Fact]
    public void IsReady_TrueOnlyWhenBothConfirmed()
    {
        var s = new ProjectState { EnvironmentConfirmed = true, SolutionConfirmed = true };
        Assert.True(s.IsReady);
    }

    // ─── FullPrefix: PublisherPrefix + "_" ───

    [Fact]
    public void FullPrefix_AppendsUnderscoreToPublisherPrefix()
    {
        var s = new ProjectState { PublisherPrefix = "adc" };
        Assert.Equal("adc_", s.FullPrefix);
    }

    [Fact]
    public void FullPrefix_ReturnsBareUnderscoreWhenPrefixIsEmpty()
    {
        // Edge: an unconfigured state still has a defined FullPrefix; callers should
        // gate on PublisherConfirmed / PublisherPrefix.Length, not on FullPrefix.
        var s = new ProjectState();
        Assert.Equal("_", s.FullPrefix);
    }

    // ─── Partial-class composition: setting any field doesn't break others ───

    [Fact]
    public void Partials_AllSettersWriteIntoTheSameInstance()
    {
        // Each partial-class file adds properties to the same class. This test would
        // catch a future refactor that accidentally split ProjectState into separate
        // classes.
        var s = new ProjectState
        {
            // identity (ProjectState.cs)
            ProjectId = "p1",
            ProjectName = "Demo",

            // environment (ProjectState.Environment.cs)
            EnvironmentUrl = "https://contoso.crm.dynamics.com",
            EnvironmentConfirmed = true,

            // solution (ProjectState.Solution.cs)
            SolutionUniqueName = "DemoSol",
            SolutionConfirmed = true,
            PublisherPrefix = "adc",
        };

        Assert.Equal("p1", s.ProjectId);
        Assert.Equal("https://contoso.crm.dynamics.com", s.EnvironmentUrl);
        Assert.Equal("DemoSol", s.SolutionUniqueName);
        Assert.True(s.IsReady);
        Assert.Equal("adc_", s.FullPrefix);
    }

    [Fact]
    public void OperationStack_BehavesLikeAStack()
    {
        var s = new ProjectState();
        s.OperationStack.Push(new ExecutedOperation { OperationId = "op1", ToolName = "ExecuteCreateTable" });
        s.OperationStack.Push(new ExecutedOperation { OperationId = "op2", ToolName = "ExecuteCreateField" });

        Assert.Equal(2, s.OperationStack.Count);
        Assert.Equal("op2", s.OperationStack.Peek().OperationId); // LIFO

        var popped = s.OperationStack.Pop();
        Assert.Equal("op2", popped.OperationId);
        Assert.Single(s.OperationStack);
    }

    [Fact]
    public void PlansAndExecutedPlans_AreIndependentDictionaries()
    {
        // Symptom this guards: a future refactor accidentally aliases the two maps,
        // making "did this plan execute?" a tautology.
        var s = new ProjectState();
        s.PendingPlans["plan-a"] = "{}";

        Assert.Single(s.PendingPlans);
        Assert.Empty(s.ExecutedPlans);
        Assert.False(s.ExecutedPlans.ContainsKey("plan-a"));
    }

    // ─── PendingPlan TTL ───

    [Fact]
    public void AddPendingPlan_WritesPayloadAndTimestamp_InLockstep()
    {
        var s = new ProjectState();
        s.AddPendingPlan("plan-x", "{\"k\":1}");

        Assert.Equal("{\"k\":1}", s.PendingPlans["plan-x"]);
        Assert.True(s.PendingPlanTimestamps.ContainsKey("plan-x"));
    }

    [Fact]
    public void PruneStalePendingPlans_DropsEntriesOlderThanTtl()
    {
        var s = new ProjectState();
        var now = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
        // Two-day-old plan + one-hour-old plan.
        s.PendingPlans["old"] = "{}";
        s.PendingPlanTimestamps["old"] = now.AddDays(-2);
        s.PendingPlans["fresh"] = "{}";
        s.PendingPlanTimestamps["fresh"] = now.AddHours(-1);

        var pruned = s.PruneStalePendingPlans(now);

        Assert.Single(pruned);
        Assert.Equal("old", pruned[0]);
        Assert.False(s.PendingPlans.ContainsKey("old"));
        Assert.False(s.PendingPlanTimestamps.ContainsKey("old"));
        Assert.True(s.PendingPlans.ContainsKey("fresh"));
    }

    [Fact]
    public void PruneStalePendingPlans_KeepsEntriesWithoutTimestamp_BackCompat()
    {
        // Sessions saved before the TTL feature have no timestamp for their
        // pending plans. We can't tell their age — preserve them so we don't
        // delete user data on first load after upgrade.
        var s = new ProjectState();
        s.PendingPlans["legacy"] = "{}";
        // Note: PendingPlanTimestamps is left empty for "legacy".

        var pruned = s.PruneStalePendingPlans(DateTime.UtcNow);

        Assert.Empty(pruned);
        Assert.True(s.PendingPlans.ContainsKey("legacy"));
    }

    [Fact]
    public void PruneStalePendingPlans_OnEmptyDict_IsNoOp()
    {
        var s = new ProjectState();
        var pruned = s.PruneStalePendingPlans(DateTime.UtcNow);
        Assert.Empty(pruned);
    }

    // ─── Orphan sweep: AddPendingPlan drops same-kind orphans ───

    private static string MakePlanId(string kind) =>
        kind + Guid.NewGuid().ToString("N"); // kind ends with "_"

    [Fact]
    public void AddPendingPlan_PreservesRecentPlansOfSameKind_LegitimateBatch()
    {
        // The single biggest pending-plan regression: agent plans 3+ fields in
        // a batch (each create_field_*), then executes them. If the sweep
        // dropped same-kind plans unconditionally, only the LAST plan would
        // survive — exactly what happened in a real session where 3 of 4
        // batched fields silently failed with "plan not found".
        // The sweep MUST keep recent same-kind plans.
        var s = new ProjectState();
        var firstFieldPlan = MakePlanId("create_field_");
        var secondFieldPlan = MakePlanId("create_field_");
        var thirdFieldPlan = MakePlanId("create_field_");

        s.AddPendingPlan(firstFieldPlan, "{\"v\":1}");
        s.AddPendingPlan(secondFieldPlan, "{\"v\":2}");
        s.AddPendingPlan(thirdFieldPlan, "{\"v\":3}");

        // All three plans coexist — the agent can execute them in any order.
        Assert.True(s.PendingPlans.ContainsKey(firstFieldPlan));
        Assert.True(s.PendingPlans.ContainsKey(secondFieldPlan));
        Assert.True(s.PendingPlans.ContainsKey(thirdFieldPlan));
    }

    [Fact]
    public void AddPendingPlan_SweepsStaleSameKindPlans_AcrossTurns()
    {
        // Older orphans (>5 min) get cleaned up when a fresh plan of the same
        // kind arrives. Test by backdating the timestamp of an older plan.
        var s = new ProjectState();
        var oldPlan = MakePlanId("update_form_");
        s.AddPendingPlan(oldPlan, "{}");
        // Backdate this plan to 10 minutes ago — past the 5-min threshold.
        s.PendingPlanTimestamps[oldPlan] = DateTime.UtcNow - TimeSpan.FromMinutes(10);

        var newPlan = MakePlanId("update_form_");
        s.AddPendingPlan(newPlan, "{}");

        // Stale same-kind plan was swept; new plan kept.
        Assert.False(s.PendingPlans.ContainsKey(oldPlan));
        Assert.True(s.PendingPlans.ContainsKey(newPlan));
    }

    [Fact]
    public void AddPendingPlan_PreservesPlansOfDifferentKinds()
    {
        // Same-kind sweep must NOT cross artifact types — a new chart plan
        // shouldn't drop a pending form plan that's still legitimately waiting.
        var s = new ProjectState();
        var formPlan = MakePlanId("update_form_");
        var chartPlan = MakePlanId("create_chart_");

        s.AddPendingPlan(formPlan, "{}");
        s.AddPendingPlan(chartPlan, "{}");

        Assert.True(s.PendingPlans.ContainsKey(formPlan));
        Assert.True(s.PendingPlans.ContainsKey(chartPlan));
    }

    [Fact]
    public void AddPendingPlan_DoesNotSweepExecutedPlans()
    {
        // ExecutedPlans is the audit trail — it must never be touched by the
        // sweep, even when a fresh plan of the same kind comes through.
        var s = new ProjectState();
        var executed = "update_form_" + Guid.NewGuid().ToString("N");
        s.ExecutedPlans[executed] = "{\"committed\":true}";

        s.AddPendingPlan(MakePlanId("update_form_"), "{}");

        Assert.True(s.ExecutedPlans.ContainsKey(executed));
    }

    [Fact]
    public void AddPendingPlan_MalformedId_DoesNotSweepAnything()
    {
        // A malformed planId can't be classified into a kind — defensively, we
        // skip the sweep entirely rather than guess. Existing plans stay put.
        var s = new ProjectState();
        var existing = MakePlanId("update_form_");
        s.AddPendingPlan(existing, "{}");

        s.AddPendingPlan("not-a-real-plan-id", "{}");

        Assert.True(s.PendingPlans.ContainsKey(existing));
        Assert.True(s.PendingPlans.ContainsKey("not-a-real-plan-id"));
    }
}
