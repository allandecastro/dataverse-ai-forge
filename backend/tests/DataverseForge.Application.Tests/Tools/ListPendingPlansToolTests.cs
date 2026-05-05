using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ListPendingPlansToolTests
{
    private static (ListPendingPlansTool tool, ProjectState state) Build()
    {
        var state = new ProjectState();
        var store = new Mock<IProjectStateStore>();
        store.Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        var ctx = new Mock<ISessionContext>();
        ctx.SetupGet(c => c.SessionId).Returns("test-session");
        return (new ListPendingPlansTool(store.Object, ctx.Object), state);
    }

    [Fact]
    public async Task ListPendingPlans_Empty_ReturnsEmptyList()
    {
        var (tool, _) = Build();
        var result = await tool.ListPendingPlansAsync(CancellationToken.None);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListPendingPlans_ReturnsPlanIdAndKindAndAge()
    {
        var (tool, state) = Build();
        // Plan a form 30s ago.
        var formPlanId = "create_form_" + Guid.NewGuid().ToString("N");
        state.PendingPlans[formPlanId] = "{}";
        state.PendingPlanTimestamps[formPlanId] = DateTime.UtcNow - TimeSpan.FromSeconds(30);

        var result = await tool.ListPendingPlansAsync(CancellationToken.None);

        var entry = Assert.Single(result);
        Assert.Equal(formPlanId, entry.PlanId);
        Assert.Equal("create_form", entry.Kind);
        // Allow a small clock-drift window — should be ~30s.
        Assert.InRange(entry.AgeSeconds, 28, 35);
    }

    [Fact]
    public async Task ListPendingPlans_OrdersByAgeAscending_NewestFirst()
    {
        // Sorting by age ascending means the smallest age first — i.e. the
        // most recently created plan appears at index 0. Useful for the
        // agent: when scanning a list, the freshest plan is on top.
        var (tool, state) = Build();
        var oldPlan = "create_field_" + Guid.NewGuid().ToString("N");
        var newPlan = "create_field_" + Guid.NewGuid().ToString("N");
        state.PendingPlans[oldPlan] = "{}";
        state.PendingPlanTimestamps[oldPlan] = DateTime.UtcNow - TimeSpan.FromMinutes(2);
        state.PendingPlans[newPlan] = "{}";
        state.PendingPlanTimestamps[newPlan] = DateTime.UtcNow - TimeSpan.FromSeconds(10);

        var result = await tool.ListPendingPlansAsync(CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(newPlan, result[0].PlanId);
        Assert.Equal(oldPlan, result[1].PlanId);
    }

    [Fact]
    public async Task ListPendingPlans_PlanWithoutTimestamp_ReturnsAgeMinusOne()
    {
        // Back-compat: plans saved before timestamp tracking shipped don't
        // have an entry in PendingPlanTimestamps. The tool reports age=-1
        // so the agent can tell "unknown age" apart from "0 seconds old".
        var (tool, state) = Build();
        var planId = "create_field_" + Guid.NewGuid().ToString("N");
        state.PendingPlans[planId] = "{}";
        // No timestamp entry — simulating legacy state.

        var result = await tool.ListPendingPlansAsync(CancellationToken.None);
        Assert.Equal(-1, result.Single().AgeSeconds);
    }

    [Fact]
    public async Task ListPendingPlans_MalformedPlanId_ReportsKindUnknown()
    {
        var (tool, state) = Build();
        state.PendingPlans["bad-plan-id"] = "{}";
        state.PendingPlanTimestamps["bad-plan-id"] = DateTime.UtcNow;

        var result = await tool.ListPendingPlansAsync(CancellationToken.None);
        Assert.Equal("unknown", result.Single().Kind);
    }
}
