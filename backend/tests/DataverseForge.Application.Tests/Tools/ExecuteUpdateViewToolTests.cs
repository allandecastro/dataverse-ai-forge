using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

/// <summary>
/// Covers the 2026-05-25 patch (LookupPlan + ALREADY_EXECUTED + F-23
/// ApplyAndSaveAsync), plus the GUID parsing guard.
/// </summary>
public class ExecuteUpdateViewToolTests
{
    private const string PlanId = "update_view_test";
    private const string PayloadJson = "{\"fetchxml\":\"<fetch/>\"}";
    private static readonly Guid ViewId = Guid.Parse("12345678-1234-1234-1234-123456789012");

    private readonly Mock<IDataverseClient> _client = new();
    private readonly Mock<IProjectStateStore> _store = new();
    private readonly Mock<ISessionContext> _ctx = new();

    private ExecuteUpdateViewTool CreateTool() => new(
        _client.Object, _store.Object, _ctx.Object,
        NullLogger<ExecuteUpdateViewTool>.Instance);

    private ProjectState Setup(string sessionId, bool pending = true, bool alreadyExecuted = false)
    {
        var state = new ProjectState { ProjectId = sessionId };
        if (pending) state.PendingPlans[PlanId] = PayloadJson;
        if (alreadyExecuted) state.ExecutedPlans[PlanId] = PayloadJson;
        _ctx.Setup(c => c.SessionId).Returns(sessionId);
        _store.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);
        return state;
    }

    [Fact]
    public async Task HappyPath_CallsUpdate_MovesPlanToExecuted_PushesOp()
    {
        var state = Setup("uv-1");
        var tool = CreateTool();

        var result = await tool.ExecuteUpdateViewAsync(
            PlanId, ViewId.ToString(), "adc_workorder", "Active Work Orders", CancellationToken.None);

        _client.Verify(c => c.UpdateViewAsync(
            ViewId, PayloadJson, It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(state.PendingPlans.ContainsKey(PlanId));
        Assert.True(state.ExecutedPlans.ContainsKey(PlanId));
        Assert.Single(state.OperationStack);
        Assert.Contains("handoff_to_tester", result);
    }

    [Fact]
    public async Task AlreadyExecuted_ShortCircuits()
    {
        Setup("uv-2", pending: false, alreadyExecuted: true);
        var tool = CreateTool();

        var result = await tool.ExecuteUpdateViewAsync(
            PlanId, ViewId.ToString(), "adc_workorder", "Active Work Orders", CancellationToken.None);

        Assert.StartsWith("ALREADY_EXECUTED:", result);
        _client.Verify(c => c.UpdateViewAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")] // empty GUID rejected
    public async Task InvalidViewId_ReturnsError_WithoutLookup(string badId)
    {
        Setup("uv-3", pending: false);
        var tool = CreateTool();

        var result = await tool.ExecuteUpdateViewAsync(
            PlanId, badId, "adc_workorder", "view", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("viewId", result);
        _store.Verify(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PlanNotFound_ReturnsError_WithoutCallingClient()
    {
        Setup("uv-4", pending: false);
        var tool = CreateTool();

        var result = await tool.ExecuteUpdateViewAsync(
            "missing_plan", ViewId.ToString(), "adc_workorder", "view", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        _client.Verify(c => c.UpdateViewAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
