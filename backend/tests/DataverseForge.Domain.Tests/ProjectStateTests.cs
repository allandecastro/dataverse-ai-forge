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
}
