using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

/// <summary>
/// The "safe cancel" tool for stale planIds. Critical because the DMB / SB /
/// UIB prompts now point agents here when handoff_to_tester refuses with
/// "plans waiting to be executed" (see CLAUDE.md pattern #8 + the DMB
/// PENDING-PLANS GUARD section). A regression in this tool would either
/// break recovery or — worse — invite the original "agent invents destructive
/// cancellations" bug back.
/// </summary>
public class DiscardPendingPlanToolTests
{
    private readonly Mock<IProjectStateStore> _store = new();
    private readonly Mock<ISessionContext> _ctx = new();

    private DiscardPendingPlanTool CreateTool() =>
        new(_store.Object, _ctx.Object, NullLogger<DiscardPendingPlanTool>.Instance);

    private ProjectState Setup(string sessionId, params (string planId, string payload)[] pending)
    {
        var state = new ProjectState { ProjectId = sessionId };
        foreach (var (id, payload) in pending)
        {
            state.PendingPlans[id] = payload;
            state.PendingPlanTimestamps[id] = DateTime.UtcNow;
        }
        _ctx.Setup(c => c.SessionId).Returns(sessionId);
        _store.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);
        return state;
    }

    [Fact]
    public async Task SinglePlanId_Removes_AndLeavesOthersAlone()
    {
        var state = Setup("s1", ("plan_a", "{}"), ("plan_b", "{}"));
        var tool = CreateTool();

        var result = await tool.DiscardPendingPlanAsync("plan_a", "scope changed", CancellationToken.None);

        Assert.StartsWith("OK:", result);
        Assert.False(state.PendingPlans.ContainsKey("plan_a"));
        Assert.True(state.PendingPlans.ContainsKey("plan_b"));
        Assert.False(state.PendingPlanTimestamps.ContainsKey("plan_a"));
        Assert.Single(state.AuditLog);
        _store.Verify(s => s.SaveAsync("s1", state, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CommaList_RemovesAllSpecified()
    {
        var state = Setup("s2", ("plan_a", "{}"), ("plan_b", "{}"), ("plan_c", "{}"));
        var tool = CreateTool();

        var result = await tool.DiscardPendingPlanAsync("plan_a, plan_c", "batch cleanup", CancellationToken.None);

        Assert.StartsWith("OK:", result);
        Assert.False(state.PendingPlans.ContainsKey("plan_a"));
        Assert.False(state.PendingPlans.ContainsKey("plan_c"));
        Assert.True(state.PendingPlans.ContainsKey("plan_b"));
    }

    [Fact]
    public async Task LiteralAll_RemovesEverything()
    {
        var state = Setup("s3", ("plan_a", "{}"), ("plan_b", "{}"), ("plan_c", "{}"));
        var tool = CreateTool();

        var result = await tool.DiscardPendingPlanAsync("all", "stale batch — replanning", CancellationToken.None);

        Assert.StartsWith("OK:", result);
        Assert.Empty(state.PendingPlans);
        Assert.Contains("You can now hand off to Tester", result);
    }

    [Fact]
    public async Task NoMatches_ReturnsNoop_WithoutSaving()
    {
        Setup("s4", ("plan_a", "{}"));
        var tool = CreateTool();

        var result = await tool.DiscardPendingPlanAsync("plan_not_here", "unused", CancellationToken.None);

        Assert.StartsWith("NOOP:", result);
        _store.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmptyPendingPlans_ReturnsNoop_Early()
    {
        Setup("s5"); // no pending plans
        var tool = CreateTool();

        var result = await tool.DiscardPendingPlanAsync("anything", "cleanup", CancellationToken.None);

        Assert.StartsWith("NOOP:", result);
        Assert.Contains("PendingPlans is empty", result);
    }

    [Fact]
    public async Task EmptyPlanIdInput_ReturnsError()
    {
        Setup("s6", ("plan_a", "{}"));
        var tool = CreateTool();

        var result = await tool.DiscardPendingPlanAsync("", "no input", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        _store.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
