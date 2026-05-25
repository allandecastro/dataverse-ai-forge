using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

/// <summary>
/// Locks in the 2026-05-25 patch: LookupPlan() instead of TryGetValue() so
/// the ALREADY_EXECUTED early-return matches the other Execute* tools, plus
/// the standard F-23 ApplyAndSaveAsync contract (state changes survive
/// concurrent writes).
/// </summary>
public class ExecuteModifyTableToolTests
{
    private const string PlanId = "modify_table_test";
    private const string PayloadJson = "{\"DisplayCollectionName\":{\"LocalizedLabels\":[]}}";

    private readonly Mock<IDataverseClient> _client = new();
    private readonly Mock<IProjectStateStore> _store = new();
    private readonly Mock<ISessionContext> _ctx = new();

    private ExecuteModifyTableTool CreateTool() => new(
        _client.Object, _store.Object, _ctx.Object,
        NullLogger<ExecuteModifyTableTool>.Instance);

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
    public async Task HappyPath_CallsModify_RemovesFromPending_PushesOp()
    {
        var state = Setup("mt-1");
        var tool = CreateTool();

        var result = await tool.ExecuteModifyTableAsync(
            PlanId, "adc_workorder", CancellationToken.None);

        _client.Verify(c => c.ModifyTableAsync(
            "adc_workorder", PayloadJson, It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(state.PendingPlans.ContainsKey(PlanId));
        Assert.True(state.ExecutedPlans.ContainsKey(PlanId));
        Assert.Single(state.OperationStack);
        Assert.Equal("ExecuteModifyTable", state.OperationStack.Peek().ToolName);
        Assert.Contains("handoff_to_tester", result);
    }

    [Fact]
    public async Task AlreadyExecuted_ShortCircuits_WithoutCallingClient()
    {
        // The 2026-05-25 patch added LookupPlan() so this branch fires when
        // the planId is in ExecutedPlans (re-run protection).
        Setup("mt-2", pending: false, alreadyExecuted: true);
        var tool = CreateTool();

        var result = await tool.ExecuteModifyTableAsync(
            PlanId, "adc_workorder", CancellationToken.None);

        Assert.StartsWith("ALREADY_EXECUTED:", result);
        _client.Verify(c => c.ModifyTableAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PlanNotFound_ReturnsError()
    {
        Setup("mt-3", pending: false);
        var tool = CreateTool();

        var result = await tool.ExecuteModifyTableAsync(
            "missing_plan", "adc_workorder", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("missing_plan", result);
        _client.Verify(c => c.ModifyTableAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RejectedPlanIdPrefix_ReturnsSkipped_WithoutLookup()
    {
        Setup("mt-4", pending: false);
        var tool = CreateTool();

        var result = await tool.ExecuteModifyTableAsync(
            "rejected_modify_table_xyz", "adc_workorder", CancellationToken.None);

        Assert.StartsWith("SKIPPED:", result);
        _client.Verify(c => c.ModifyTableAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LowercasesTableLogicalName_BeforeCall()
    {
        // Dataverse logical names are lowercase; the tool normalizes mixed-case
        // input. Recurring bug pattern #3 in CLAUDE.md.
        Setup("mt-5");
        var tool = CreateTool();

        await tool.ExecuteModifyTableAsync(
            PlanId, "Adc_WorkOrder", CancellationToken.None);

        _client.Verify(c => c.ModifyTableAsync(
            "adc_workorder", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
